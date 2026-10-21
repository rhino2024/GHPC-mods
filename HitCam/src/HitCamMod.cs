using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GHPC;
using ShellInfo = GHPC.Weapons.ShotInfo;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HitCam.HitCamMod), "Hit Cam", "1.0.0", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace HitCam
{
    public sealed class HitCamMod : MelonMod
    {
        sealed class Request
        {
            public ShellInfo Shot;
            public IUnit Victim;
        }

        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<bool> eEnabled, eInterior, eInfoText, eFlightPath, eDebugLog;
        static MelonPreferences_Entry<float> eDuration, eOrbitSpeed, eFov, eElevation, eDistanceScale, eFlightLeg;
        static MelonPreferences_Entry<int> eWidth, eHeight, eMinCaliber, eMargin;
        static MelonPreferences_Entry<KeyCode> eToggle;

        static readonly List<Request> queue = new List<Request>();
        static ReplaySession active;
        static StreamWriter log;
        static int loggedRejections;

        public static float Duration { get { return eDuration.Value; } }
        public static float OrbitSpeed { get { return eOrbitSpeed.Value; } }
        public static float FieldOfView { get { return eFov.Value; } }
        public static float Elevation { get { return eElevation.Value; } }
        public static float DistanceScale { get { return eDistanceScale.Value; } }
        public static int WindowWidth { get { return eWidth.Value; } }
        public static int WindowHeight { get { return eHeight.Value; } }
        public static int Margin { get { return eMargin.Value; } }
        public static bool ShowInterior { get { return eInterior.Value; } }
        public static bool ShowInfoText { get { return eInfoText.Value; } }
        public static bool DebugLogging { get { return eDebugLog != null && eDebugLog.Value; } }
        public static bool ShowFlightPath { get { return eFlightPath.Value; } }
        public static float FlightLegMeters { get { return eFlightLeg.Value; } }

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("HitCam", "Hit Cam");
            eEnabled = cat.CreateEntry<bool>("Enabled", true, "Master switch");
            eDuration = cat.CreateEntry<float>("Duration", 3f, "Seconds the replay window stays open");
            eWidth = cat.CreateEntry<int>("WindowWidth", 800, "Replay texture width");
            eHeight = cat.CreateEntry<int>("WindowHeight", 450, "Replay texture height");
            eMargin = cat.CreateEntry<int>("Margin", 24, "Distance from the top right corner, in pixels");
            eMinCaliber = cat.CreateEntry<int>("MinCaliberMm", 40, "Hide the window for shells below this caliber");
            eOrbitSpeed = cat.CreateEntry<float>("OrbitSpeed", 26f, "Camera orbit speed, degrees per second");
            eElevation = cat.CreateEntry<float>("Elevation", 10f, "Camera elevation angle, degrees above the target");
            eDistanceScale = cat.CreateEntry<float>("DistanceScale", 0.72f, "Multiplier on how far the replay camera sits from the target");
            eFov = cat.CreateEntry<float>("FieldOfView", 24f, "Replay camera field of view");
            eInterior = cat.CreateEntry<bool>("ShowInterior", true, "Highlight the struck vehicle's AarVisual interior art in the window");
            eInfoText = cat.CreateEntry<bool>("ShowInfoText", true, "Show ammo / distance / penetration caption");
            eFlightPath = cat.CreateEntry<bool>("ShowFlightPath", false, "Show the long gun-to-target flight trace line");
            eFlightLeg = cat.CreateEntry<float>("FlightLegMeters", 25f, "Trace legs longer than this are the flight path and get hidden");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.F9, "Toggles the mod at runtime");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false, "Write diagnostic lines to Bin/Mods/HitCam.debug.log; only turn this on when something is wrong");

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("Hit Cam loaded (debug log " + (eDebugLog.Value ? "on" : "off") + ")");
            Log("Hit Cam initialised (patch target: LiveRound.doDestroy)");
            SelfCheck();
        }

        // silent failure is the worst outcome here: both members are private, so a
        // game update that renames them disables the mod without any exception
        void SelfCheck()
        {
            if (HarmonyLib.AccessTools.Field(typeof(GHPC.Weapons.LiveRound), "_didDestroy") == null)
                Log("SELF-CHECK FAIL: LiveRound._didDestroy missing, hook will never fire");
            if (HarmonyLib.AccessTools.Method(typeof(AarController), "createShotTrace") == null)
                Log("SELF-CHECK FAIL: AarController.createShotTrace missing, no impact visuals");
            if (AarController.Instance == null)
                Log("note: no AarController in scene yet (normal until a mission loads)");
        }

        public override void OnUpdate()
        {
            AarProbe.Tick(Time.unscaledDeltaTime);

            if (Input.GetKeyDown(eToggle.Value))
            {
                eEnabled.Value = !eEnabled.Value;
                eEnabled.Save();
                Log("toggled " + (eEnabled.Value ? "ON" : "OFF"));
                if (!eEnabled.Value) StopActive();
            }

            if (active != null && !active.Update(Time.unscaledDeltaTime)) return;
            if (active != null)
            {
                active.Dispose();
                active = null;
                Log("window closed");
            }

            if (active == null && queue.Count > 0 && eEnabled.Value)
            {
                var request = queue[0];
                queue.RemoveAt(0);
                active = ReplaySession.TryStart(request.Shot, request.Victim);
                Log(active != null ? "window opened" : "window FAILED to open");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            StopActive();
            queue.Clear();
            ReplaySession.ForgetProbes();
            Log("scene loaded: " + sceneName);
        }

        void StopActive()
        {
            if (active == null) return;
            active.Dispose();
            active = null;
        }

        internal static void OnShotTerminated(ShellInfo shot)
        {
            if (shot == null) return;

            string rejection = Reject(shot);
            IUnit victim = rejection == null ? FindVictim(shot) : null;
            if (victim == null && rejection == null) rejection = "no hostile vehicle struck";

            if (rejection != null)
            {
                if (loggedRejections++ < 40) Log("skip (" + loggedRejections + "): " + rejection + " | " + Describe(shot));
                return;
            }

            Log("hit accepted -> " + Describe(shot) + " vs " + victim.FriendlyName);
            queue.Clear();
            queue.Add(new Request { Shot = shot, Victim = victim });
        }

        static string Reject(ShellInfo shot)
        {
            if (!eEnabled.Value) return "mod disabled";
            if (shot.ParentShot != null) return "child shot (spall)";
            if (!shot.IsPlayerShot) return "not the player's round";
            if (!shot.IsHit) return "no hit recorded";
            if (shot.TypeInfo == null) return "ammo type missing";
            float caliber = shot.TypeInfo.GetFinalCaliber();
            if (caliber < eMinCaliber.Value) return "caliber " + caliber.ToString("0") + "mm < " + eMinCaliber.Value;
            return null;
        }

        static IUnit FindVictim(ShellInfo shot)
        {
            if (shot.HitUnits == null) return null;
            var shooter = shot.Shooter;
            for (int i = 0; i < shot.HitUnits.Count; i++)
            {
                var unit = shot.HitUnits[i];
                if (unit == null || unit.Chassis == null) continue;
                if (shooter != null && unit.Allegiance == shooter.Allegiance) continue;
                return unit;
            }
            return null;
        }

        static string Describe(ShellInfo shot)
        {
            if (shot == null) return "<null>";
            string ammo = shot.TypeInfo != null ? shot.TypeInfo.Name : "?";
            return ammo + " d=" + shot.Distance.ToString("0") + "m pen=" + shot.ActualPen.ToString("0") + "mm";
        }

        static void OpenLog()
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                log = new StreamWriter(Path.Combine(dir, "HitCam.debug.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
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
