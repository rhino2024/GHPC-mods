using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(WreckCleanup.WreckCleanupMod), "残骸清理", "1.0.2", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace WreckCleanup
{
    // Burning hulls pile up and cost frames. Once the field holds this many vehicle wrecks - the
    // default is three - the mod switches them off, so their rendering, fire particles and physics all
    // stop. It is a threshold, not a trickle: below it nothing is touched at all.
    //
    // Wrecks are deactivated rather than destroyed. GHPC.World.SceneUnitsManager only rebuilds its unit
    // lists when they are empty, so a Destroy() would leave it and every AI that has a target reference
    // holding a dead object; SetActive(false) keeps the references valid and takes the unit out of
    // FindObjectsOfType on the next sweep.
    public sealed class WreckCleanupMod : MelonMod
    {
        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<bool> eEnabled, eHideOnMap, eRestoreInAar, eDebugLog;
        static MelonPreferences_Entry<int> eThreshold;
        static MelonPreferences_Entry<float> eWreckDelay, eCheckInterval;
        static MelonPreferences_Entry<KeyCode> eToggle;

        static StreamWriter log;
        static float sweepTimer;
        static bool inMission;

        // instance id -> game time at which the sweep first saw that unit destroyed
        static readonly Dictionary<int, float> seenDestroyed = new Dictionary<int, float>();
        // wrecks this mod switched off, kept so the AAR can have them back
        static readonly List<GHPC.Unit> hiddenWrecks = new List<GHPC.Unit>();
        static readonly Dictionary<int, bool> hideOnMapBefore = new Dictionary<int, bool>();

        static int cleanedTotal;

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("WreckCleanup", "残骸清理");
            eEnabled = cat.CreateEntry<bool>("Enabled", true, "Clean up automatically once the field holds enough wrecks");
            eThreshold = cat.CreateEntry<int>("WreckThreshold", 3, "How many vehicle wrecks have to be on the field before they are cleared");
            eWreckDelay = cat.CreateEntry<float>("WreckDelay", 8f, "A wreck has to burn this long before it counts as clearable, so the kill is still watchable");
            eCheckInterval = cat.CreateEntry<float>("CheckInterval", 1f, "Seconds between field sweeps");
            eHideOnMap = cat.CreateEntry<bool>("HideOnMap", true, "Also mark the wreck as hidden so no stale icon is left on the tactical map");
            eRestoreInAar = cat.CreateEntry<bool>("RestoreInAar", true, "Show the wrecks again while the after action replay runs, since it films them, and hide them again afterwards");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.F8, "Switch the cleanup on or off during a mission");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false, "Write sweep diagnostics to Bin/Mods/WreckCleanup.debug.log; only turn this on when something is wrong");

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("Wreck Cleanup loaded (threshold " + eThreshold.Value
                + ", delay " + eWreckDelay.Value + " s, " + (eEnabled.Value ? "on" : "off") + ")");
            Log("Wreck Cleanup initialised");
        }

        public override void OnUpdate()
        {
            if (!inMission) return;

            if (Input.GetKeyDown(eToggle.Value))
            {
                eEnabled.Value = !eEnabled.Value;
                try { cat.SaveToFile(false); } catch { }
                MelonLogger.Msg("Wreck Cleanup " + (eEnabled.Value ? "enabled" : "disabled"));
                Log("toggled -> " + (eEnabled.Value ? "enabled" : "disabled"));
            }

            if (!eEnabled.Value) return;

            if (SafeInAar())
            {
                if (eRestoreInAar.Value && hiddenWrecks.Count > 0) RestoreHidden();
                return;
            }

            sweepTimer -= Time.deltaTime;
            if (sweepTimer > 0f) return;
            sweepTimer = Mathf.Clamp(eCheckInterval.Value, 0.25f, 10f);
            Sweep();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            seenDestroyed.Clear();
            hiddenWrecks.Clear();
            hideOnMapBefore.Clear();
            sweepTimer = 0f;
            inMission = !string.IsNullOrEmpty(sceneName)
                && sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) < 0
                && sceneName.IndexOf("LOADER", StringComparison.OrdinalIgnoreCase) < 0;
            Log("scene loaded: " + sceneName);
        }

        public override void OnApplicationQuit()
        {
            if (log == null) return;
            try { log.Dispose(); } catch { }
            log = null;
        }

        void Sweep()
        {
            var units = UnityEngine.Object.FindObjectsOfType<GHPC.Unit>();
            if (units == null) return;

            var playerUnit = PlayerUnit();
            int wrecks = 0;
            int vehicles = 0;
            var ready = new List<GHPC.Unit>();

            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                if (unit == null || unit.gameObject == null || !unit.gameObject.activeSelf) continue;

                var type = unit.Type;
                if (type == GHPC.UnitType.GroundVehicle || type == GHPC.UnitType.AirVehicle) vehicles++;

                if (!IsWreck(unit)) continue;

                int id = unit.GetInstanceID();
                float first;
                if (!seenDestroyed.TryGetValue(id, out first))
                {
                    seenDestroyed[id] = Time.time;
                    first = Time.time;
                }

                wrecks++;
                // the player's own hull stays whatever the game makes of it
                if (playerUnit != null && unit == playerUnit) continue;
                if (Time.time - first >= Mathf.Max(0f, eWreckDelay.Value)) ready.Add(unit);
            }

            int threshold = Math.Max(1, eThreshold.Value);
            if (eDebugLog.Value)
                Log("sweep: units=" + units.Length + " vehicles=" + vehicles + " wrecks=" + wrecks
                    + " ready=" + ready.Count + " threshold=" + threshold
                    + " hidden=" + hiddenWrecks.Count);

            if (wrecks < threshold || ready.Count == 0) return;

            for (int i = 0; i < ready.Count; i++) Hide(ready[i]);

            MelonLogger.Msg("Wreck Cleanup: cleared " + ready.Count + " wreck(s), "
                + cleanedTotal + " in this mission");
            Log("cleared " + ready.Count + " wreck(s): " + NamesOf(ready));
        }

        static bool IsWreck(GHPC.Unit unit)
        {
            // Unit.Destroyed is only ever set for helicopters (NotifyDestroyed has exactly one
            // caller); a burnt-out hull on the ground is flagged through UnitIncapacitated.
            // Neutralized is the game's own "this unit is lost" flag, the one the casualty counts
            // and the tactical map read, so it covers both.
            if (!unit.Neutralized && !unit.Destroyed) return false;
            var type = unit.Type;
            return type == GHPC.UnitType.GroundVehicle || type == GHPC.UnitType.AirVehicle;
        }

        void Hide(GHPC.Unit unit)
        {
            try
            {
                int id = unit.GetInstanceID();
                if (!hideOnMapBefore.ContainsKey(id)) hideOnMapBefore[id] = unit.HideOnMap;
                if (eHideOnMap.Value) unit.HideOnMap = true;
                unit.gameObject.SetActive(false);
                hiddenWrecks.Add(unit);
                cleanedTotal++;
            }
            catch (Exception e)
            {
                Log("hiding " + unit.name + " threw: " + e.Message);
            }
        }

        // The after action replay films the vehicles it lost, including the ones already cleared, so
        // they are switched back on for its duration.
        void RestoreHidden()
        {
            int restored = 0;
            for (int i = 0; i < hiddenWrecks.Count; i++)
            {
                var unit = hiddenWrecks[i];
                if (unit == null || unit.gameObject == null) continue;
                try
                {
                    unit.gameObject.SetActive(true);
                    bool before;
                    if (hideOnMapBefore.TryGetValue(unit.GetInstanceID(), out before))
                        unit.HideOnMap = before;
                    restored++;
                }
                catch (Exception e) { Log("restoring " + unit.name + " threw: " + e.Message); }
            }

            hiddenWrecks.Clear();
            hideOnMapBefore.Clear();
            seenDestroyed.Clear();
            Log("AAR: restored " + restored + " wreck(s)");
        }

        static string NamesOf(List<GHPC.Unit> units)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < units.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(UnitLabel(units[i]));
            }
            return sb.ToString();
        }

        static string UnitLabel(GHPC.Unit unit)
        {
            string name;
            try { name = string.IsNullOrEmpty(unit.UniqueName) ? unit.FriendlyName : unit.UniqueName; }
            catch { name = unit.name; }
            var pos = unit.transform != null ? unit.transform.position : Vector3.zero;
            return name + "@" + pos.x.ToString("0") + "," + pos.z.ToString("0");
        }

        static GHPC.Unit PlayerUnit()
        {
            try { return GHPC.Player.PlayerInput.Instance != null ? GHPC.Player.PlayerInput.Instance.CurrentPlayerUnit : null; }
            catch { return null; }
        }

        static bool SafeInAar()
        {
            try { return GHPC.AarController.InAar; }
            catch { return false; }
        }

        static void OpenLog()
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                log = new StreamWriter(Path.Combine(dir, "WreckCleanup.debug.log"), true,
                    new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch { log = null; }
        }

        internal static void Log(object message)
        {
            if (log == null) return;
            try { log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message); }
            catch { }
        }
    }
}
