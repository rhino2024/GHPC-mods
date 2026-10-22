using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GHPC.Equipment;
using GHPC.Player;
using GHPC.UI.ContextMenu;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;
using UnityEngine.Events;

[assembly: MelonInfo(typeof(RepairMenu.RepairMenuMod), "载具维修菜单", "1.0.2", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace RepairMenu
{
    // Adds a Repair submenu to the game's native Q (Context Menu). It lists the damaged chassis
    // modules and repairs one of them, or all of them, on click - the work then takes some seconds,
    // scaled so that a full repair never costs more than the configured budget. Health alone is not
    // enough for destroyed parts: the chassis keeps its own broken/parking-brake state and the
    // transmission type is overwritten when it is killed, so those are restored when the timer runs
    // out.
    public sealed class RepairMenuMod : MelonMod
    {
        enum CoreModule { Engine, Transmission, Radiator, LeftTrack, RightTrack }

        struct Repairable
        {
            public CoreModule Module;
            public GHPC.Equipment.DestructibleComponent Component;
            public float StartHealth;
        }

        // A repair is claimed by a menu click and finished some seconds later, so the crew gets the
        // wait time rather than an instant fix. One job at a time, one vehicle.
        sealed class Job
        {
            public PlayerInput Pi;
            public GHPC.Unit Unit;
            public ChassisDamageManager Damage;
            public CoreModule? Only;
            public List<Repairable> Targets;
            public float Duration;
            public float Elapsed;
        }

        // weights add up to FullRepairWeight, so "Repair/All" on a wreck of a tank costs exactly the
        // configured budget and a single module costs its share of it
        const float FullRepairWeight = 30f;

        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<bool> eDebugLog, eShowProgress;
        static MelonPreferences_Entry<float> eRepairSeconds;
        static StreamWriter log;
        static Job job;

        // the Q menu root is one shared object that PlayerInput instantiates in Awake, and the game
        // only empties it through ContextMenuBuilder.Clear - which the death / new-vehicle paths can
        // skip. So the entries are rebuilt from this signature instead of from "does a Repair node
        // already exist", which is a test that can latch permanently.
        static string lastSignature = "";
        static int lastAdded;

        static readonly FieldInfo BuilderField =
            AccessTools.Field(typeof(PlayerInput), "_contextMenuBuilder");
        static readonly MethodInfo CloseMenuMethod =
            AccessTools.Method(typeof(PlayerInput), "ContextMenuButtonClicked");
        static readonly MethodInfo UpdateDamageTextMethod =
            AccessTools.Method(typeof(ChassisDamageManager), "updateDamageText");

        static readonly FieldInfo BrokenField =
            AccessTools.Field(typeof(GHPC.NwhChassis), "_broken");
        static readonly FieldInfo ParkingBrakeField =
            AccessTools.Field(typeof(GHPC.NwhChassis), "_parkingBrake");
        static readonly FieldInfo PowerField =
            AccessTools.Field(typeof(EquipmentManager), "_powerOn");
        static readonly FieldInfo EquipmentDamagedField =
            AccessTools.Field(typeof(EquipmentManager), "<Damaged>k__BackingField");
        static readonly FieldInfo PlatformPowerField =
            AccessTools.Field(typeof(GHPC.AimablePlatform), "<PowerEnabled>k__BackingField");

        static readonly FieldInfo EngineDestroyedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_engineDestroyed");
        static readonly FieldInfo EngineDamagedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_engineDamaged");
        static readonly FieldInfo TransmissionDestroyedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_transmissionDestroyed");
        static readonly FieldInfo TransmissionDamagedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_transmissionDamaged");
        static readonly FieldInfo RadiatorDestroyedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_radiatorDestroyed");
        static readonly FieldInfo LeftTrackDestroyedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_leftTrackDestroyed");
        static readonly FieldInfo RightTrackDestroyedField =
            AccessTools.Field(typeof(ChassisDamageManager), "_rightTrackDestroyed");

        static readonly Dictionary<int, int> originalTransmission = new Dictionary<int, int>();

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("RepairMenu", "载具维修菜单");
            eRepairSeconds = cat.CreateEntry<float>("RepairSeconds", 30f,
                "How long repairing every module takes; one module costs its share of that. Set 0 for an instant fix");
            eShowProgress = cat.CreateEntry<bool>("ShowProgress", true,
                "Show the remaining repair time at the bottom of the screen while a repair is running");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false,
                "Write repair diagnostics to Bin/Mods/RepairMenu.debug.log; only turn this on when something is wrong");

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("Repair Menu loaded (debug log " + (eDebugLog.Value ? "on" : "off") + ")");
            Log("Repair Menu initialised; menu wiring " + WiringState());
        }

        public override void OnUpdate()
        {
            RememberPlayerTransmission();
            TickRepair();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            originalTransmission.Clear();
            job = null;
            // the shared Q menu root of the old mission is gone; the signature must not be reused
            lastSignature = "";
            lastAdded = 0;
            Log("scene loaded: " + sceneName);
        }

        public override void OnApplicationQuit()
        {
            if (log == null) return;
            try { log.Dispose(); } catch { }
            log = null;
        }

        static string WiringState()
        {
            return "builder=" + (BuilderField != null)
                + " close=" + (CloseMenuMethod != null)
                + " damageText=" + (UpdateDamageTextMethod != null)
                + " nwhBroken=" + (BrokenField != null)
                + " parkingBrake=" + (ParkingBrakeField != null)
                + " power=" + (PowerField != null);
        }

        // The Q menu is rebuilt from scratch every time it opens. FinalizeContextMenu runs after all
        // of the game's own Add*Buttons calls and before it sorts the entries, so buttons added here
        // land in a properly ordered submenu and disappear with the rest when the menu closes.
        internal static void Inject(PlayerInput pi)
        {
            try
            {
                if (pi == null) return;
                var unit = pi.CurrentPlayerUnit;
                if (unit == null) { Log("inject skipped: no player unit"); return; }

                var damage = unit.GetComponentInChildren<ChassisDamageManager>(true);
                if (damage == null) { Log("inject skipped: no chassis damage under " + unit.name); return; }

                var builder = BuilderField != null
                    ? BuilderField.GetValue(pi) as ContextMenuBuilder
                    : null;
                if (builder == null) { Log("inject skipped: no context menu builder"); return; }

                var root = pi.ContextMenuRoot;
                if (root == null) { Log("inject skipped: no context menu root"); return; }

                var damaged = DamagedModules(damage);
                if (damaged.Count == 0 && job == null)
                {
                    // nothing to offer, so leave no trace: a healthy tank shows its own menu
                    lastSignature = "";
                    return;
                }

                var labels = new List<string>();
                var actions = new List<UnityAction>();

                // one crew can only turn one wrench: while a job is running the submenu offers the
                // remaining time and a way to back out of it
                if (job != null)
                {
                    bool sameUnit = job.Unit == unit;
                    labels.Add(sameUnit
                        ? "Repair/" + DescribeTargets(job.Targets) + " - " + RemainingLabel(job) + ", click to cancel"
                        : "Repair/busy on another vehicle");
                    Job captured = job;
                    actions.Add(delegate { if (sameUnit) CancelRepair(captured); });
                }
                else
                {
                    labels.Add("Repair/All (" + SecondsLabel(damaged) + ")");
                    actions.Add(delegate { Repair(pi, null); });

                    for (int i = 0; i < damaged.Count; i++)
                    {
                        CoreModule module = damaged[i].Module;
                        var single = new List<Repairable> { damaged[i] };
                        labels.Add("Repair/" + ModuleName(module)
                            + " (" + HealthLabel(damaged[i].Component) + ", " + SecondsLabel(single) + ")");
                        actions.Add(delegate { Repair(pi, module); });
                    }
                }

                // the unit is part of the signature: two tanks of the same type damaged in the same
                // places would otherwise let the old rows keep pointing at the vehicle the player left
                string signature = unit.name + "|" + string.Join(";", labels.ToArray());
                if (signature == lastSignature)
                {
                    // the rows the menu is holding right now are exactly these ones; the game calls
                    // this every frame the menu stays open
                    pi.ContextMenuButtonCount += lastAdded;
                    return;
                }

                var stale = root.transform.Find("Repair");
                if (stale != null)
                {
                    // DestroyImmediate, not Destroy: the builder looks its children up by name in this
                    // same frame and would otherwise adopt the rows that are about to die
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }

                UnityAction close = delegate { CloseMenu(pi); };
                for (int i = 0; i < labels.Count; i++)
                    builder.AddButton(close, labels[i], actions[i], false);

                lastSignature = signature;
                lastAdded = labels.Count;
                pi.ContextMenuButtonCount += labels.Count;
                Log("menu built: " + labels.Count + " repair entries, damaged=" + damaged.Count
                    + " unit=" + unit.name);
            }
            catch (Exception e)
            {
                Log("menu inject threw: " + e);
            }
        }

        static List<Repairable> DamagedModules(ChassisDamageManager damage)
        {
            var list = new List<Repairable>();
            AddIfDamaged(list, CoreModule.Engine, damage.EngineHitZone);
            AddIfDamaged(list, CoreModule.Transmission, damage.TransmissionHitZone);
            AddIfDamaged(list, CoreModule.Radiator, damage.RadiatorHitZone);
            AddIfDamaged(list, CoreModule.LeftTrack, damage.LeftTrackHitZone);
            AddIfDamaged(list, CoreModule.RightTrack, damage.RightTrackHitZone);
            return list;
        }

        static void AddIfDamaged(List<Repairable> list, CoreModule module, GHPC.Equipment.DestructibleComponent component)
        {
            if (component == null || component.HealthPercent >= 99.9f) return;
            list.Add(new Repairable
            {
                Module = module,
                Component = component,
                StartHealth = Mathf.Clamp(component.HealthPercent, 0f, 100f)
            });
        }

        static void Repair(PlayerInput pi, CoreModule? only)
        {
            try
            {
                if (job != null) return;
                var unit = pi != null ? pi.CurrentPlayerUnit : null;
                if (unit == null) return;

                var damage = unit.GetComponentInChildren<ChassisDamageManager>(true);
                if (damage == null) return;

                var list = DamagedModules(damage);
                list = KeepSelected(list, only);
                if (list.Count == 0) { Log("repair clicked but nothing is damaged"); return; }

                float seconds = SecondsFor(list);
                if (seconds <= 0f) { Finish(unit, damage, only, list); return; }

                job = new Job
                {
                    Pi = pi,
                    Unit = unit,
                    Damage = damage,
                    Only = only,
                    Targets = list,
                    Duration = seconds
                };
                MelonLogger.Msg("Repair Menu: repairing in " + seconds.ToString("0.0") + " s");
                Log("repair started: " + DescribeTargets(list) + " taking " + seconds.ToString("0.0")
                    + " s (budget " + eRepairSeconds.Value.ToString("0.0") + " s)");
            }
            catch (Exception e)
            {
                Log("repair click threw: " + e);
            }
        }

        // The wait is the whole point, so the crew is shown healing work rather than a frozen screen:
        // module health climbs towards 100 % while the timer runs. Everything that is not pure health
        // - damage flags, gearbox type, engine running, power, the chassis' own broken state - is only
        // restored when the timer is up.
        static void TickRepair()
        {
            var j = job;
            if (j == null) return;

            j.Elapsed += Time.deltaTime;
            float left = j.Duration - j.Elapsed;
            if (left <= 0f)
            {
                job = null;
                Finish(j.Unit, j.Damage, j.Only, j.Targets);
                return;
            }

            if (j.Damage == null || j.Damage.gameObject == null)
            {
                job = null;
                Log("repair aborted: the vehicle is gone");
                return;
            }
            if (j.Unit != null && j.Unit.Destroyed)
            {
                job = null;
                Log("repair aborted: vehicle destroyed during the work");
                return;
            }

            float t = Mathf.Clamp01(j.Elapsed / j.Duration);
            for (int i = 0; i < j.Targets.Count; i++)
            {
                var component = j.Targets[i].Component;
                if (component == null || component.gameObject == null) continue;
                float from = j.Targets[i].StartHealth;
                try { component.SetHealthPercent(Mathf.Lerp(from, 100f, t)); }
                catch (Exception e)
                {
                    job = null;
                    Log("repair ramp threw: " + e.Message);
                    return;
                }
            }
        }

        static void CancelRepair(Job j)
        {
            if (j == null || job != j) return;
            job = null;
            Log("repair cancelled with " + (j.Duration - j.Elapsed).ToString("0.0") + " s left");
        }

        // The partial healing is kept when the job is cancelled; only the mechanics that have to be
        // applied in one go (flags, gearbox, engine, power) wait for the timer.
        static void Finish(GHPC.Unit unit, ChassisDamageManager damage, CoreModule? only,
            List<Repairable> targets)
        {
            try
            {
                if (unit == null || damage == null || damage.gameObject == null) return;

                bool engineWasDestroyed = IsDestroyed(damage.EngineHitZone);
                bool transmissionWasDestroyed = IsDestroyed(damage.TransmissionHitZone);
                var chassis = unit.GetComponentInChildren<GHPC.IChassis>(true);

                int repaired = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    var component = targets[i].Component;
                    if (component == null || component.gameObject == null) continue;
                    if (component.HealthPercent >= 99.9f) continue;
                    try
                    {
                        component.SetHealthPercent(100f);
                        repaired++;
                    }
                    catch (Exception e)
                    {
                        Log("repair " + ModuleName(targets[i].Module) + " threw: " + e.Message);
                    }
                }

                ClearModuleFlags(damage, only);
                try { UpdateDamageTextMethod?.Invoke(damage, null); } catch { }

                var nwh = chassis as GHPC.NwhChassis;
                if (nwh != null)
                {
                    if (Affected(only, CoreModule.Transmission))
                    {
                        try { chassis.SetTransmissionIntegrityPercent(100f); } catch { }
                        RestoreTransmissionType(nwh, transmissionWasDestroyed);
                    }

                    if (Affected(only, CoreModule.Engine))
                    {
                        try { chassis.SetEnginePowerPercent(100f); } catch { }
                        if (engineWasDestroyed)
                        {
                            try
                            {
                                var engine = nwh.VehicleController != null
                                    ? nwh.VehicleController.engine
                                    : null;
                                if (engine != null && !engine.IsRunning) engine.Start();
                            }
                            catch (Exception e) { Log("engine start threw: " + e.Message); }
                            RestorePower(unit);
                        }
                    }

                    // _broken is shared by a dead engine and either broken track. Only a repair
                    // that touches mobility may clear it, and only once every one of those parts
                    // is healthy again; otherwise the tank would drive with a track still off.
                    bool touchesMobility = only == null
                        || only == CoreModule.Engine
                        || only == CoreModule.LeftTrack
                        || only == CoreModule.RightTrack;
                    bool criticalDown = IsDestroyed(damage.EngineHitZone)
                        || IsDestroyed(damage.LeftTrackHitZone)
                        || IsDestroyed(damage.RightTrackHitZone);
                    if (touchesMobility && !criticalDown)
                    {
                        SetField(BrokenField, nwh, false);
                        SetField(ParkingBrakeField, nwh, false);
                        RestoreAbs(nwh);
                    }
                }

                MelonLogger.Msg("Repair Menu: repair done"
                    + (only != null ? " [" + ModuleName(only.Value) + "]" : " [all]"));
                Log("repair finished, health applied to " + repaired + " module(s)"
                    + (only != null ? " [" + ModuleName(only.Value) + "]" : " [all]")
                    + " engineDestroyedBefore=" + engineWasDestroyed
                    + " transmissionDestroyedBefore=" + transmissionWasDestroyed
                    + " totalHealth=" + HealthSummary(damage));
            }
            catch (Exception e)
            {
                Log("repair finish threw: " + e);
            }
        }

        static List<Repairable> KeepSelected(List<Repairable> list, CoreModule? only)
        {
            if (only == null) return list;
            var kept = new List<Repairable>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].Module == only.Value) kept.Add(list[i]);
            return kept;
        }

        static float WeightOf(CoreModule module)
        {
            switch (module)
            {
                case CoreModule.Engine: return 8f;
                case CoreModule.Transmission: return 8f;
                case CoreModule.Radiator: return 4f;
                default: return 5f;   // either track
            }
        }

        static float SecondsFor(List<Repairable> list)
        {
            float budget = Mathf.Clamp(eRepairSeconds != null ? eRepairSeconds.Value : 30f, 0f, 300f);
            if (budget <= 0f) return 0f;

            float weight = 0f;
            for (int i = 0; i < list.Count; i++) weight += WeightOf(list[i].Module);
            return Mathf.Max(0.5f, budget * weight / FullRepairWeight);
        }

        static string SecondsLabel(List<Repairable> list)
        {
            return SecondsFor(list).ToString("0") + " s";
        }

        static string DescribeTargets(List<Repairable> list)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(ModuleName(list[i].Module));
            }
            return sb.ToString();
        }

        static string RemainingLabel(Job j)
        {
            return Mathf.Max(0f, j.Duration - j.Elapsed).ToString("0") + " s left";
        }

        public override void OnGUI()
        {
            var j = job;
            if (j == null || eShowProgress == null || !eShowProgress.Value) return;

            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            string text = "Repairing " + DescribeTargets(j.Targets) + " - " + RemainingLabel(j);
            float width = 380f;
            float height = 26f;
            var rect = new Rect(Screen.width * 0.5f - width * 0.5f,
                Screen.height - height - 120f, width, height);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(rect, text, ProgressStyle());

            GUI.matrix = matrix;
        }

        static GUIStyle progressStyle;

        static GUIStyle ProgressStyle()
        {
            if (progressStyle != null) return progressStyle;
            progressStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            progressStyle.normal.textColor = Color.white;
            return progressStyle;
        }

        static void ClearModuleFlags(ChassisDamageManager damage, CoreModule? only)
        {
            if (Affected(only, CoreModule.Engine))
            {
                SetField(EngineDestroyedField, damage, false);
                SetField(EngineDamagedField, damage, false);
            }
            if (Affected(only, CoreModule.Transmission))
            {
                SetField(TransmissionDestroyedField, damage, false);
                SetField(TransmissionDamagedField, damage, false);
            }
            if (Affected(only, CoreModule.Radiator))
                SetField(RadiatorDestroyedField, damage, false);
            if (Affected(only, CoreModule.LeftTrack))
                SetField(LeftTrackDestroyedField, damage, false);
            if (Affected(only, CoreModule.RightTrack))
                SetField(RightTrackDestroyedField, damage, false);
        }

        static bool Affected(CoreModule? only, CoreModule module)
        {
            return only == null || only.Value == module;
        }

        static bool IsDestroyed(GHPC.Equipment.DestructibleComponent component)
        {
            return component != null && component.IsDestroyed;
        }

        static void SetField(FieldInfo field, object target, object value)
        {
            if (field == null || target == null) return;
            try { field.SetValue(target, value); }
            catch (Exception e) { Log("field " + field.Name + " threw: " + e.Message); }
        }

        // KillTransmission switches the NWH gearbox to Manual; putting the original type back is
        // what makes an automatic tank shift again after the transmission is repaired.
        static void RestoreTransmissionType(GHPC.NwhChassis nwh, bool wasDestroyed)
        {
            if (!wasDestroyed) return;
            var controller = nwh.VehicleController;
            if (controller == null || controller.transmission == null) return;

            int original;
            if (!originalTransmission.TryGetValue(nwh.GetInstanceID(), out original) || original < 0)
                original = (int)Transmission.TransmissionType.Automatic;

            try
            {
                var type = (Transmission.TransmissionType)original;
                if (controller.transmission.transmissionType != type)
                    controller.transmission.transmissionType = type;
            }
            catch (Exception e)
            {
                Log("transmission type restore threw: " + e.Message);
            }
        }

        static void RestoreAbs(GHPC.NwhChassis nwh)
        {
            try
            {
                var controller = nwh.VehicleController;
                if (controller != null && controller.drivingAssists != null
                    && controller.drivingAssists.abs != null)
                    controller.drivingAssists.abs.enabled = true;
            }
            catch (Exception e)
            {
                Log("ABS restore threw: " + e.Message);
            }
        }

        // A destroyed engine calls EquipmentManager.KillPower, which turns the lamps off, drops
        // PowerEnabled on the aiming platforms and has no inverse in the game.
        static void RestorePower(GHPC.Unit unit)
        {
            try
            {
                var equipment = unit.GetComponentInChildren<EquipmentManager>(true);
                if (equipment != null)
                {
                    SetField(EquipmentDamagedField, equipment, false);
                    SetField(PowerField, equipment, true);
                }

                var platforms = unit.GetComponentsInChildren<GHPC.AimablePlatform>(true);
                for (int i = 0; i < platforms.Length; i++)
                    SetField(PlatformPowerField, platforms[i], true);
            }
            catch (Exception e)
            {
                Log("power restore threw: " + e.Message);
            }
        }

        static void RememberPlayerTransmission()
        {
            var pi = Player();
            var unit = pi != null ? pi.CurrentPlayerUnit : null;
            if (unit == null) return;

            var nwh = unit.GetComponentInChildren<GHPC.NwhChassis>(true);
            if (nwh == null) return;
            var controller = nwh.VehicleController;
            if (controller == null || controller.transmission == null) return;

            int id = nwh.GetInstanceID();
            if (originalTransmission.ContainsKey(id)) return;

            bool broken = false;
            if (BrokenField != null)
            {
                try { broken = (bool)BrokenField.GetValue(nwh); }
                catch { }
            }

            // Seen already broken: the type on the gearbox is the post-kill Manual, not the real
            // original, so mark it unknown and let the repair choose a sane default.
            originalTransmission[id] = broken ? -1 : (int)controller.transmission.transmissionType;
        }

        static PlayerInput Player()
        {
            try { return PlayerInput.Instance; }
            catch { return null; }
        }

        static void CloseMenu(PlayerInput pi)
        {
            try { CloseMenuMethod?.Invoke(pi, null); }
            catch (Exception e) { Log("menu close threw: " + e.Message); }
        }

        static string ModuleName(CoreModule module)
        {
            switch (module)
            {
                case CoreModule.Engine: return "Engine";
                case CoreModule.Transmission: return "Transmission";
                case CoreModule.Radiator: return "Radiator";
                case CoreModule.LeftTrack: return "Left Track";
                case CoreModule.RightTrack: return "Right Track";
                default: return "?";
            }
        }

        static string HealthLabel(GHPC.Equipment.DestructibleComponent component)
        {
            if (component == null) return "?";
            if (component.IsDestroyed) return "destroyed";
            return Mathf.RoundToInt(component.HealthPercent) + "%";
        }

        static string HealthSummary(ChassisDamageManager damage)
        {
            return "E" + Percent(damage.EngineHitZone)
                + " T" + Percent(damage.TransmissionHitZone)
                + " R" + Percent(damage.RadiatorHitZone)
                + " L" + Percent(damage.LeftTrackHitZone)
                + " R" + Percent(damage.RightTrackHitZone);
        }

        static string Percent(GHPC.Equipment.DestructibleComponent component)
        {
            return component == null ? "-" : Mathf.RoundToInt(component.HealthPercent).ToString();
        }

        static void OpenLog()
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                log = new StreamWriter(Path.Combine(dir, "RepairMenu.debug.log"), true,
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

    [HarmonyPatch(typeof(GHPC.Player.PlayerInput), "FinalizeContextMenu")]
    internal static class FinalizeContextMenuPatch
    {
        static void Prefix(GHPC.Player.PlayerInput __instance)
        {
            RepairMenuMod.Inject(__instance);
        }
    }
}
