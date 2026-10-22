using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SlewReticle.SlewReticleMod), "第三人称瞄准环", "1.0.4", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace SlewReticle
{
    // War-Thunder style third person, automatic: whenever the game is in its exterior camera, a
    // fixed white circle sits in the middle of the screen, a second circle marks where the muzzle is
    // actually pointing, and the turret is slewed to whatever the first circle is over. The exterior
    // camera itself is parked at the turret instead of far behind the hull. Leaving the exterior view
    // (or binoculars / free cam / the AAR) switches it off again.
    // Commander view keeps the slew without the circles, and holding Z or the mouse right button pulls
    // the exterior view in through the game's own zoom steps.
    //
    // The whole thing runs through the live gameplay classes - GHPC.Player.PlayerInput,
    // GHPC.Camera.CameraManager and GHPC.Crew.CrewBrainWeaponsModule. The global PlayerControl /
    // SlewableTurret / BufferedCameraFollow types an IL dump points at are obsolete stubs with zero
    // instances in a running mission.
    public sealed class SlewReticleMod : MelonMod
    {
        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<KeyCode> eToggle, eZoomKey;
        static MelonPreferences_Entry<bool> eSlew, eRing, eDebugLog;
        static MelonPreferences_Entry<bool> eHideRingCommander, eZoomMouse;
        static MelonPreferences_Entry<bool> eCamAtTurret, eShowGunRing;
        static MelonPreferences_Entry<float> eRingScale, eRingWidth, eAimDistance;
        static MelonPreferences_Entry<float> eRingOffset, eZoomExtra;
        static MelonPreferences_Entry<float> eTurretBack, eTurretUp, eGunRingScale, eGunRingWidth;
        static MelonPreferences_Entry<string> eRingColor, eGunRingColor;

        static bool featureOn = true;
        static bool activeNow;
        static Texture2D ringTexture;
        static Texture2D gunRingTexture;
        static float ringPixelSize;
        static float gunRingPixelSize;
        static StreamWriter log;

        // what the textures were last built from, so a rebuild only happens on a real change
        static int builtScreenHeight;
        static string builtRingColor, builtGunColor;
        static float builtRingWidth, builtRingScale, builtGunWidth, builtGunScale;

        static Vector3 lastAimDir;
        static float reportTimer;
        static string lastHitLabel = "-";
        static float lastHitDistance = -1f;
        static GHPC.Player.PlayerInput cachedInput;
        static bool inMission;
        static bool harvested;
        static string pendingKeys = "";
        static string lastView = "";
        static float viewTimer;
        static float tuneSaveTimer;
        static bool commanderNow;
        static bool zoomHeld;
        static SlewReticleMod self;
        static string noticeText = "";
        static float noticeTimer;

        // the FOV boost is written into the live camera every frame, so the value the game itself asked
        // for is remembered separately - writing back the boosted value would otherwise compound
        static float zoomBaseFov;
        static float zoomWrittenFov;

        // the turret anchor is added onto wherever the game put the camera this frame, so the vehicle's
        // own motion stays handled by its follow smoothing; only this offset is smoothed by the mod
        static Vector3 anchorDelta;
        static Vector3 anchorDeltaVel;
        static bool anchorLive;
        static GHPC.AimablePlatform anchorPlatform;
        static bool anchorNotice;
        static float camTuneSaveTimer;

        // switching crew sits the player in another seat, which makes the game hand the turret to
        // the gunner AI; these let the mod keep aim authority for as long as it is on
        static readonly FieldInfo SeatField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_controlledPosition");
        static readonly FieldInfo FullControlField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_hasFullVehicleControl");
        static readonly FieldInfo UnitAIField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_unitAI");
        static readonly FieldInfo SweepField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.AI.GunnerAIController), "_sweepEnabled");

        // the follow camera's own obstacle layer mask, reused for the turret anchor so the parked camera
        // cannot end up inside a wall or under the ground
        static readonly FieldInfo CollisionMaskField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Camera.BufferedCameraFollow), "collisionMask");

        int lastSeat = int.MinValue;
        bool lastFullControl;
        GHPC.AI.UnitAI suppressedUnit;
        GHPC.AI.GunnerAIController suppressedSweeper;
        bool suppressedSweep;
        bool aiSuppressed;
        bool reassertAI;

        public override void OnInitializeMelon()
        {
            self = this;
            cat = MelonPreferences.CreateCategory("SlewReticle", "第三人称瞄准环");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.F7, "Turn the whole feature on or off; while on it follows the exterior view by itself. A notice flashes on screen whenever this key changes the state, so a stray press cannot silently kill the circle");
            eSlew = cat.CreateEntry<bool>("AutoSlew", true, "Slew the turret to whatever the screen centre ray hits");
            eRing = cat.CreateEntry<bool>("ShowRing", true, "Draw the fixed screen-centre circle");
            eRingScale = cat.CreateEntry<float>("RingScale", 0.055f, "Circle diameter as a fraction of screen height");
            eRingWidth = cat.CreateEntry<float>("RingWidth", 3f, "Circle line width in pixels");
            eRingColor = cat.CreateEntry<string>("RingColor", "FFFFFF", "Circle colour, RRGGBB hex");
            eAimDistance = cat.CreateEntry<float>("AimDistance", 2000f, "How far the centre ray reaches when it hits nothing");
            eRingOffset = cat.CreateEntry<float>("RingOffsetY", 0.18f, "Circle height above the true screen centre, as a fraction of screen height; the aim ray leaves from the circle. Nudge it live with Keypad + / -");
            eHideRingCommander = cat.CreateEntry<bool>("HideRingInCommander", true, "Commander view keeps the turret slew but draws no circle and aims from the true screen centre");
            eShowGunRing = cat.CreateEntry<bool>("ShowGunRing", true, "Draw a second, smaller circle at the spot the muzzle is actually pointing at, so the turret still catching up is visible");
            eGunRingScale = cat.CreateEntry<float>("GunRingScale", 0.032f, "Muzzle circle diameter as a fraction of screen height");
            eGunRingWidth = cat.CreateEntry<float>("GunRingWidth", 2.5f, "Muzzle circle line width in pixels");
            eGunRingColor = cat.CreateEntry<string>("GunRingColor", "FFFFFF", "Muzzle circle colour, RRGGBB hex");
            eCamAtTurret = cat.CreateEntry<bool>("CameraAtTurret", true, "Holding the zoom key in third person parks the camera at the turret instead of behind the hull; releasing it, or leaving third person, gives the camera back to the game. The camera keeps the aim direction the game gave it, only its position moves. Tune it live: Keypad * / push it back and forward, Keypad 9 / 7 raise and lower it");
            eTurretBack = cat.CreateEntry<float>("TurretBackDistance", 5f, "Metres behind the turret the third person camera sits when CameraAtTurret is on");
            eTurretUp = cat.CreateEntry<float>("TurretUpHeight", 1.6f, "Metres above the turret the third person camera sits when CameraAtTurret is on");
            eZoomKey = cat.CreateEntry<KeyCode>("ZoomKey", KeyCode.Z, "Hold this to pull the exterior view in, release to go back to the base zoom");
            eZoomMouse = cat.CreateEntry<bool>("ZoomOnMouse", true, "The mouse right button does the same hold-to-zoom; turn this off if it clashes with a binding of your own");
            eZoomExtra = cat.CreateEntry<float>("ZoomExtraFov", 0.4f, "Extra magnification on top of the game's own strongest exterior zoom, while the zoom key is held: the camera field of view is multiplied by this. 0.4 is 2.5 times as much magnification, 1 disables the extra step");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false, "Write the view / slew diagnostics to Bin/Mods/SlewReticle.debug.log; only turn this on when something is wrong");

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("Slew Reticle loaded (debug log " + (eDebugLog.Value ? "on" : "off") + ")");
            SyncRing();
            Log("Slew Reticle initialised (follows the exterior view; " + eToggle.Value + " disables it)");
        }

        public override void OnUpdate()
        {
            // diagnostics only: the view probe walks the scene, so it stays off unless the log is wanted
            if (eDebugLog.Value)
            {
                ProbeKeys();
                Watch();
            }
            TuneRingOffset();
            TuneCameraAnchor();
            if (Input.GetKeyDown(eToggle.Value))
            {
                featureOn = !featureOn;
                // the circle vanishing because of one stray key press reads as a bug, so say out loud
                // on screen what the key just did
                ShowNotice(featureOn
                    ? "Slew Reticle ON (" + eToggle.Value + ")"
                    : "Slew Reticle OFF - press " + eToggle.Value + " to bring the circle back");
                Log("feature " + (featureOn ? "enabled" : "disabled"));
            }
            if (noticeTimer > 0f) noticeTimer -= Time.unscaledDeltaTime;

            var pi = featureOn ? FindInput() : null;
            SetActive(pi);
            WatchSeat(pi);
            commanderNow = CommanderMode();
            HandleZoom(activeNow);

            if (!activeNow) return;
            SyncRing();
            if (eDebugLog.Value) Report(pi);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            SetActive(null);
            cachedInput = null;
            lastSeat = int.MinValue;
            lastFullControl = false;
            harvested = false;
            // the follow camera of the old scene is gone, so the held zoom is simply forgotten
            zoomHeld = false;
            zoomBaseFov = 0f;
            zoomWrittenFov = 0f;
            commanderNow = false;
            anchorLive = false;
            anchorPlatform = null;
            anchorDelta = Vector3.zero;
            anchorDeltaVel = Vector3.zero;
            lastHitDistance = -1f;
            inMission = !string.IsNullOrEmpty(sceneName)
                && sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) < 0
                && sceneName.IndexOf("LOADER", StringComparison.OrdinalIgnoreCase) < 0;
            Log("scene loaded: " + sceneName);
        }

        // the feature switches itself on and off with the view the player is in, so no key has to be
        // remembered; the toggle key only disables the whole thing for the session
        void SetActive(GHPC.Player.PlayerInput pi)
        {
            bool want = pi != null
                && Exterior(pi)
                && !SafeInAar()
                && !Binoculars(pi)
                && !FreeFly();

            if (want == activeNow) return;
            activeNow = want;
            anchorLive = false;
            anchorPlatform = null;
            if (want)
            {
                lastAimDir = Vector3.zero;
                reportTimer = 0f;
                SyncRing();
                if (eCamAtTurret.Value && !anchorNotice)
                {
                    anchorNotice = true;
                    ShowNotice("Hold " + eZoomKey.Value + " / right mouse for the view from the turret"
                        + " - Keypad * / back and forward, Keypad 9 / 7 up and down");
                }
            }
            else RestoreGunnerAI();
            Log("slew " + (want ? "ON" : "off") + " | " + LiveState(pi)
                + (want ? " ring=" + (ringTexture != null ? ringTexture.width + "px" : "none")
                    + " gunRing=" + (eShowGunRing.Value ? (gunRingTexture != null ? gunRingTexture.width + "px" : "none") : "off")
                    + " offsetY=" + eRingOffset.Value
                    + " camAtTurret=" + (eCamAtTurret.Value ? 1 : 0)
                    + " back=" + eTurretBack.Value.ToString("0.0") + "m up=" + eTurretUp.Value.ToString("0.0") + "m"
                    + " gunnerAI=" + AiState(pi) : ""));
        }

        // "switch crew member" is not a camera action: SwitchRoles only moves the player to another
        // seat and lets UnitAI re-enable every crew brain, the gunner's included. An AI gunner then
        // sweeps and aims the same FCS from its own Update, so the circle stays on screen but the
        // turret stops following it. While the mod is on it owns the aim, so the gunner brain is
        // parked for the duration and handed back untouched when the mod leaves.
        void WatchSeat(GHPC.Player.PlayerInput pi)
        {
            int seat = SeatOf(pi);
            bool full = FullControlOf(pi);

            if (seat != lastSeat || full != lastFullControl)
            {
                bool first = lastSeat == int.MinValue;
                lastSeat = seat;
                lastFullControl = full;
                if (!first && pi != null)
                {
                    // the game turned the gunner AI back on as part of the switch; the claim has
                    // to be re-issued, not just held
                    reassertAI = true;
                    Log("crew seat -> " + SeatName(seat) + " fullControl=" + (full ? 1 : 0)
                        + " active=" + (activeNow ? 1 : 0) + " gunnerAI=" + AiState(pi)
                        + " | " + LiveState(pi));
                }
            }

            if (pi == null)
            {
                if (!activeNow) RestoreGunnerAI();
                return;
            }

            if (activeNow) SuppressGunnerAI(pi);
            else RestoreGunnerAI();
        }

        void SuppressGunnerAI(GHPC.Player.PlayerInput pi)
        {
            var unit = UnitAIOf(pi);
            if (unit == null || unit.GunnerAI == null) return;
            if (aiSuppressed && suppressedUnit == unit && !reassertAI) return;

            var sweeper = unit.GunnerAIController;
            bool claimOnThisUnit = !aiSuppressed || suppressedUnit != unit;
            if (claimOnThisUnit)
            {
                if (aiSuppressed) RestoreGunnerAI(true);
                bool sweep = false;
                if (sweeper != null && SweepField != null)
                {
                    try { sweep = (bool)SweepField.GetValue(sweeper); }
                    catch { }
                }
                suppressedSweep = sweep;
            }
            reassertAI = false;

            try
            {
                if (sweeper != null) sweeper.StopSweep();
                unit.SetCrewAIEnabled(GHPC.Crew.CrewPosition.Gunner, false);
                suppressedUnit = unit;
                suppressedSweeper = sweeper;
                aiSuppressed = true;
            }
            catch (Exception e)
            {
                Log("gunner AI suppress threw: " + e.Message);
            }
        }

        void RestoreGunnerAI(bool leftUnit = false)
        {
            reassertAI = false;
            if (!aiSuppressed) return;
            aiSuppressed = false;

            var unit = suppressedUnit;
            var sweeper = suppressedSweeper;
            bool sweep = suppressedSweep;
            suppressedUnit = null;
            suppressedSweeper = null;
            suppressedSweep = false;

            if (unit == null || unit.GunnerAI == null) return;

            // a unit the player just left is fully AI driven now, whatever the player's own seat
            // and control flags say
            bool want = leftUnit || GameWantsGunnerAI();
            try
            {
                unit.SetCrewAIEnabled(GHPC.Crew.CrewPosition.Gunner, want);
                if (want && sweep && sweeper != null) sweeper.ResumeSweep();
            }
            catch (Exception e)
            {
                Log("gunner AI restore threw: " + e.Message);
            }
        }

        // exactly the rule the game itself applies: full vehicle control means the player is the
        // gunner, otherwise the AI only steps aside while the gunner seat is occupied
        bool GameWantsGunnerAI()
        {
            var pi = cachedInput;
            if (FullControlOf(pi)) return false;
            return SeatOf(pi) != (int)GHPC.Crew.CrewPosition.Gunner;
        }

        static int SeatOf(GHPC.Player.PlayerInput pi)
        {
            if (pi == null || SeatField == null) return -1;
            try { return Convert.ToInt32(SeatField.GetValue(pi)); }
            catch { return -1; }
        }

        static bool FullControlOf(GHPC.Player.PlayerInput pi)
        {
            if (pi == null || FullControlField == null) return false;
            try { return (bool)FullControlField.GetValue(pi); }
            catch { return false; }
        }

        static GHPC.AI.UnitAI UnitAIOf(GHPC.Player.PlayerInput pi)
        {
            if (pi == null) return null;
            try
            {
                if (UnitAIField != null)
                {
                    var unit = UnitAIField.GetValue(pi) as GHPC.AI.UnitAI;
                    if (unit != null) return unit;
                }
            }
            catch { }

            try
            {
                var playerUnit = pi.CurrentPlayerUnit;
                return playerUnit != null
                    ? playerUnit.GetComponentInChildren<GHPC.AI.UnitAI>()
                    : null;
            }
            catch { return null; }
        }

        static string AiState(GHPC.Player.PlayerInput pi)
        {
            var unit = UnitAIOf(pi);
            if (unit == null) return "-";
            try
            {
                var gunner = unit.GunnerAI;
                if (gunner == null) return "none";
                return gunner.enabled ? "on" : "off";
            }
            catch { return "-"; }
        }

        static string SeatName(int seat)
        {
            switch (seat)
            {
                case 0: return "Commander";
                case 1: return "Driver";
                case 2: return "Gunner";
                case 3: return "Loader";
                case 4: return "CavScout";
                case 5: return "Generic";
                default: return "-";
            }
        }

        // Aim authority has to be taken after the game has aimed: PlayerInput::Update writes the FCS
        // aim from the follow camera's forward vector every frame and the turret only slews later, in
        // AimablePlatform::LateUpdate. So this runs as a postfix on that Update (Patches.cs) and hands
        // over through the setter the player's own mouse path uses - SetAimPosition, the old write here,
        // is the AI-only setter whose result the game then overwrote, which is what put the shots beside
        // the circle instead of on it.
        internal static void ApplyAimFromPatch(GHPC.Player.PlayerInput pi)
        {
            var mod = self;
            if (mod != null) mod.ApplyAim(pi);
        }

        void ApplyAim(GHPC.Player.PlayerInput pi)
        {
            if (!activeNow || !eSlew.Value) return;

            var cam = MainCam();
            if (cam == null || !cam.isActiveAndEnabled) return;

            var module = WeaponsModule(pi);
            var info = SafeWeapon(pi);
            if (module == null || info == null) return;

            var ray = cam.ScreenPointToRay(AimPixel());
            float far = Mathf.Clamp(eAimDistance.Value, 50f, 4000f);

            RaycastHit hit;
            Vector3 direction;
            if (Physics.Raycast(ray, out hit, far) && !OwnVehicle(hit.transform))
            {
                direction = hit.point - cam.transform.position;
                lastHitLabel = hit.transform.name + " " + hit.distance.ToString("0") + "m";
                lastHitDistance = hit.distance;
            }
            else
            {
                direction = ray.direction;
                lastHitLabel = "sky";
                lastHitDistance = -1f;
            }
            if (direction.sqrMagnitude < 0.0001f) return;

            lastAimDir = direction.normalized;
            try
            {
                // the fire control normalizes this and takes the range from its own laser channel, so
                // a unit direction is exactly the shape the game's third person path passes over
                module.SetAimVector(info.Role, lastAimDir, true);
                if (info.Weapon != null) module.NotifyAimOverride(info.Role);
            }
            catch (Exception e) { Log("aim write threw: " + e.Message); }
        }

        internal static void ShowNotice(string text)
        {
            noticeText = text;
            noticeTimer = 2.5f;
        }

        // The game sets the exterior camera FOV from its zoom steps in event handlers only, so a boost
        // written once would be undone by the next of those - and a boost written every frame straight
        // onto the live camera would compound onto itself. Both are avoided by keeping the game's own
        // value separately and re-deriving from it whenever the game has changed it underneath us.
        internal static void ApplyExtraZoom(Camera cam)
        {
            if (cam == null) return;

            float extra = Mathf.Clamp(eZoomExtra != null ? eZoomExtra.Value : 1f, 0.05f, 1f);
            bool boost = zoomHeld && extra < 0.999f;
            float current = cam.fieldOfView;

            if (!boost)
            {
                if (zoomWrittenFov <= 0f) return;
                // only take back what we put there; if the game has written its own FOV since, leave it
                if (Mathf.Abs(current - zoomWrittenFov) < 0.01f) cam.fieldOfView = zoomBaseFov;
                zoomWrittenFov = 0f;
                zoomBaseFov = 0f;
                return;
            }

            if (zoomWrittenFov <= 0f || Mathf.Abs(current - zoomWrittenFov) >= 0.01f) zoomBaseFov = current;
            float want = Mathf.Max(1f, zoomBaseFov * extra);
            cam.fieldOfView = want;
            zoomWrittenFov = want;
        }

        // the follow camera sits behind the hull, so the centre ray can clip our own vehicle; aiming
        // at the barrel we are standing behind would freeze the slew
        bool OwnVehicle(Transform target)
        {
            if (target == null) return false;
            var root = PlayerRoot();
            if (root == null) return false;
            return target.IsChildOf(root) || target.root == root;
        }

        Transform PlayerRoot()
        {
            var follow = FollowCam();
            try
            {
                var unit = follow != null ? follow.CurrentUnit : null;
                if (unit != null && unit.gameObject != null) return unit.gameObject.transform;
            }
            catch { }

            var pi = FindInput();
            try
            {
                var comp = pi != null ? pi.CurrentPlayerChassis as Component : null;
                if (comp != null) return comp.transform;
            }
            catch { }
            return null;
        }

        void Report(GHPC.Player.PlayerInput pi)
        {
            reportTimer -= Time.unscaledDeltaTime;
            if (reportTimer > 0f) return;
            reportTimer = 1f;

            var platform = Platform(pi);
            float err = -1f;
            if (platform != null && lastAimDir != Vector3.zero)
            {
                var gun = platform.AimVector;
                if (gun.sqrMagnitude > 0.0001f) err = Vector3.Angle(gun.normalized, lastAimDir);
            }
            var cam = MainCam();
            string ring = "-";
            if (cam != null)
            {
                Vector2 sp;
                ring = GunAimScreenPoint(cam, out sp)
                    ? sp.x.ToString("0") + "," + sp.y.ToString("0")
                    : "off screen";
            }
            Log("slew: gun-vs-centre error " + err.ToString("0.0") + " deg, centre hits " + lastHitLabel
                + ", muzzle ring at " + ring
                + ", camera " + (anchorLive && anchorPlatform != null
                    ? "at turret " + anchorPlatform.Name
                    : zoomHeld ? "turret wanted, no platform" : "the game's own")
                + (anchorLive ? " offset " + anchorDelta.ToString("F1") : ""));
        }

        // The turret spot is the zoom view: it engages while the hold-to-zoom key is down in third
        // person, and the game gets its camera straight back on release. Leaving third person has to
        // hand the position back at once rather than glide, because first person drives this same
        // camera transform and a fading offset would keep pushing it after the switch.
        internal static void AnchorCamera(GHPC.Camera.BufferedCameraFollow follow, Camera cam)
        {
            bool wantAnchor = eCamAtTurret.Value && activeNow && !commanderNow && zoomHeld;
            if (cam == null || !wantAnchor)
            {
                // released the key while still in third person: ease the offset out so the view does
                // not jump back to the hull in one frame
                if (anchorLive && activeNow && eCamAtTurret.Value && !commanderNow)
                {
                    anchorDelta = Vector3.SmoothDamp(anchorDelta, Vector3.zero, ref anchorDeltaVel,
                        0.12f, 60f, Time.unscaledDeltaTime);
                    if (anchorDelta.sqrMagnitude > 0.0004f)
                    {
                        cam.transform.position += anchorDelta;
                        return;
                    }
                }
                anchorLive = false;
                anchorDelta = Vector3.zero;
                anchorDeltaVel = Vector3.zero;
                return;
            }

            var platform = Platform(cachedInput);
            var pivot = platform != null ? platform.Transform : null;
            if (pivot == null)
            {
                anchorLive = false;
                return;
            }

            float back = Mathf.Clamp(eTurretBack.Value, 1.5f, 30f);
            float up = Mathf.Clamp(eTurretUp.Value, -1f, 10f);
            Vector3 forward = cam.transform.forward;
            Vector3 want = pivot.position - forward * back + Vector3.up * up;
            want = KeepClearOfObstacles(follow, pivot.position, want);

            Vector3 gamePos = cam.transform.position;
            // pitching the view down aims the spot under the ground, and the game's own collision pass
            // already ran against the hull anchor, so nothing else catches it: never sit the lens below
            // where the game chose to put it this frame, nor under the turret itself
            float floorY = Mathf.Max(gamePos.y, pivot.position.y - 1.5f) - 0.6f;
            if (want.y < floorY) want.y = floorY;

            Vector3 delta = want - gamePos;

            // a cut rather than a glide on the first frame, another vehicle, or a teleport
            if (!anchorLive || platform != anchorPlatform || delta.sqrMagnitude > 400f)
            {
                anchorDelta = delta;
                anchorDeltaVel = Vector3.zero;
                anchorLive = true;
                anchorPlatform = platform;
            }
            else
            {
                anchorDelta = Vector3.SmoothDamp(anchorDelta, delta, ref anchorDeltaVel, 0.12f, 60f,
                    Time.unscaledDeltaTime);
            }

            cam.transform.position = gamePos + anchorDelta;
        }

        // the game's own collision pass runs before this, against the hull anchor, so the turret spot is
        // checked again with the same layer mask - pulled short rather than left inside a wall
        static Vector3 KeepClearOfObstacles(GHPC.Camera.BufferedCameraFollow follow, Vector3 from, Vector3 to)
        {
            if (follow == null || CollisionMaskField == null) return to;
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.5f) return to;
            dir /= len;

            int mask;
            try
            {
                var layerMask = (LayerMask)CollisionMaskField.GetValue(follow);
                mask = layerMask.value;
            }
            catch { return to; }
            if (mask == 0) return to;

            RaycastHit hit;
            try
            {
                if (!Physics.Raycast(from, dir, out hit, len, mask)) return to;
            }
            catch { return to; }
            return hit.point - dir * 0.4f;
        }

        // how far the camera sits from the turret is only judgeable on screen, so nudge it live the same
        // way the circle height is
        void TuneCameraAnchor()
        {
            if (eTurretBack == null || eTurretUp == null) return;

            float dBack = 0f, dUp = 0f;
            try
            {
                if (Input.GetKey(KeyCode.KeypadMultiply)) dBack = Time.unscaledDeltaTime * 4f;
                else if (Input.GetKey(KeyCode.KeypadDivide)) dBack = -Time.unscaledDeltaTime * 4f;
                if (Input.GetKey(KeyCode.Keypad9)) dUp = Time.unscaledDeltaTime * 2.5f;
                else if (Input.GetKey(KeyCode.Keypad7)) dUp = -Time.unscaledDeltaTime * 2.5f;
            }
            catch { }
            if (dBack == 0f && dUp == 0f) return;

            bool changed = false;
            if (dBack != 0f)
            {
                float next = Mathf.Clamp(eTurretBack.Value + dBack, 1.5f, 30f);
                if (Mathf.Abs(next - eTurretBack.Value) > 0.0005f) { eTurretBack.Value = next; changed = true; }
            }
            if (dUp != 0f)
            {
                float next = Mathf.Clamp(eTurretUp.Value + dUp, -1f, 10f);
                if (Mathf.Abs(next - eTurretUp.Value) > 0.0005f) { eTurretUp.Value = next; changed = true; }
            }
            if (!changed) return;

            camTuneSaveTimer -= Time.unscaledDeltaTime;
            if (camTuneSaveTimer > 0f) return;
            camTuneSaveTimer = 1f;
            Log("camera anchor: back " + eTurretBack.Value.ToString("0.00")
                + " m, up " + eTurretUp.Value.ToString("0.00") + " m");
            try { cat.SaveToFile(false); } catch (Exception e) { Log("saving the anchor threw: " + e.Message); }
        }

        // ---------------------------------------------------------------- live object access

        GHPC.Player.PlayerInput FindInput()
        {
            // the singleton can be replaced between missions; re-check its identity before trusting
            // the cache, otherwise the seat watcher would read state off a dead object
            GHPC.Player.PlayerInput live = null;
            try { live = GHPC.Player.PlayerInput.Instance; }
            catch (Exception e) { if (cachedInput == null) Log("PlayerInput.Instance threw: " + e.Message); }
            if (live != null && live != cachedInput) cachedInput = live;

            if (cachedInput != null && cachedInput.gameObject != null) return cachedInput;
            cachedInput = null;

            var found = UnityEngine.Object.FindObjectsOfType<GHPC.Player.PlayerInput>();
            if (found != null && found.Length > 0)
            {
                for (int i = 0; i < found.Length; i++)
                    if (found[i] != null && found[i].isActiveAndEnabled) { cachedInput = found[i]; return cachedInput; }
                cachedInput = found[0];
                return cachedInput;
            }

            return null;
        }

        static GHPC.Camera.CameraManager CameraManager()
        {
            try { return GHPC.Camera.CameraManager.Instance; }
            catch { return null; }
        }

        static GHPC.Camera.BufferedCameraFollow FollowCam()
        {
            var cm = CameraManager();
            try { return cm != null ? cm.CameraFollow : null; }
            catch { return null; }
        }

        static Camera MainCam()
        {
            try
            {
                var cam = GHPC.Camera.CameraManager.MainCam;
                if (cam != null && cam.isActiveAndEnabled) return cam;
            }
            catch { }
            return Camera.main;
        }

        static bool Exterior(GHPC.Player.PlayerInput pi)
        {
            try { return pi != null && pi.IsExteriorMode; }
            catch { return false; }
        }

        // the game's own exterior slew path skips these two, so the mod must not aim over them either
        static bool Binoculars(GHPC.Player.PlayerInput pi)
        {
            try { return pi != null && pi.UsingBinoculars; }
            catch { return false; }
        }

        static bool FreeFly()
        {
            var follow = FollowCam();
            try { return follow != null && follow.IsInFreeFlyMode; }
            catch { return false; }
        }

        static bool CommanderMode()
        {
            var follow = FollowCam();
            try { return follow != null && follow.IsCommanderMode; }
            catch { return false; }
        }

        // ---------------------------------------------------------------- zoom

        // Hold Z (or the mouse right button) and the game's own zoom steps run to the strongest level;
        // letting go returns to the base level. AdjustZoom is what the mouse wheel uses, so the zoom
        // audio, the FOV events and the optic coupling all still fire as they do without the mod.
        void HandleZoom(bool canZoom)
        {
            bool held;
            try
            {
                held = (eZoomKey.Value != KeyCode.None && Input.GetKey(eZoomKey.Value))
                    || (eZoomMouse.Value && Input.GetMouseButton(1));
            }
            catch { return; }

            if (zoomHeld && !held) ReleaseZoom();
            else if (!zoomHeld && held && canZoom) BeginZoom();
        }

        void BeginZoom()
        {
            var follow = FollowCam();
            if (follow == null) return;
            try
            {
                follow.AdjustZoom(true, true);
                zoomHeld = true;
                Log("zoom in held");
            }
            catch (Exception e) { Log("zoom in threw: " + e.Message); }
        }

        void ReleaseZoom()
        {
            zoomHeld = false;
            var follow = FollowCam();
            if (follow == null) return;
            try
            {
                follow.AdjustZoom(false, true);
                Log("zoom released");
            }
            catch (Exception e) { Log("zoom release threw: " + e.Message); }
        }

        static GHPC.Crew.CrewBrainWeaponsModule WeaponsModule(GHPC.Player.PlayerInput pi)
        {
            try
            {
                var brain = pi != null ? pi.CurrentPlayerCrewBrain : null;
                return brain != null ? brain.WeaponsModule : null;
            }
            catch { return null; }
        }

        static GHPC.Weapons.WeaponSystemInfo SafeWeapon(GHPC.Player.PlayerInput pi)
        {
            try { return pi != null ? pi.CurrentPlayerWeapon : null; }
            catch { return null; }
        }

        static GHPC.AimablePlatform Platform(GHPC.Player.PlayerInput pi)
        {
            var info = SafeWeapon(pi);
            try
            {
                if (info != null && info.FCS != null && info.FCS.TurretPlatform != null) return info.FCS.TurretPlatform;
            }
            catch { }

            var root = PlayerRootStatic();
            if (root == null) return null;
            var platforms = root.GetComponents<GHPC.AimablePlatform>();
            return platforms != null && platforms.Length > 0 ? platforms[0] : null;
        }

        static Transform PlayerRootStatic()
        {
            var follow = FollowCam();
            try
            {
                var unit = follow != null ? follow.CurrentUnit : null;
                if (unit != null && unit.gameObject != null) return unit.gameObject.transform;
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------------- diagnostics

        // Which key puts the game into which view is not ours to assert: input is Rewired actions and
        // fully rebindable. So this only records what the game itself calls the current view, whenever
        // that changes or right after one of the sampled keys goes down.
        static readonly KeyCode[] SampledKeys =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.C, KeyCode.T, KeyCode.V,
            KeyCode.F, KeyCode.Tab, KeyCode.LeftControl, KeyCode.G
        };

        void ProbeKeys()
        {
            for (int i = 0; i < SampledKeys.Length; i++)
            {
                bool down = false;
                try { down = Input.GetKeyDown(SampledKeys[i]); } catch { }
                if (down) pendingKeys += (pendingKeys.Length == 0 ? "" : "+") + SampledKeys[i];
            }
        }

        void Watch()
        {
            if (!inMission) return;
            if (!harvested)
            {
                harvested = true;
                Harvest();
            }

            viewTimer -= Time.unscaledDeltaTime;
            if (viewTimer > 0f) return;
            viewTimer = 0.25f;

            var view = ViewState();
            if (view == lastView && pendingKeys.Length == 0) return;

            bool changed = view != lastView;
            lastView = view;
            Log("view " + (changed ? "changed" : "after keys") + ": " + view
                + (pendingKeys.Length == 0 ? "" : " | pressed " + pendingKeys));
            pendingKeys = "";
            if (changed) Log("  chain: " + LiveState(cachedInput));
        }

        static string ViewState()
        {
            var sb = new StringBuilder();
            try
            {
                var cm = CameraManager();
                sb.Append("cameraManager.exterior=").Append(cm != null && cm.ExteriorMode ? 1 : 0);
                var follow = FollowCam();
                sb.Append(" commander=").Append(follow != null && follow.IsCommanderMode ? 1 : 0)
                    .Append(" freeFly=").Append(follow != null && follow.IsInFreeFlyMode ? 1 : 0)
                    .Append(" binoculars=").Append(cachedInput != null && cachedInput.UsingBinoculars ? 1 : 0);
            }
            catch (Exception e)
            {
                sb.Append("<view probe threw ").Append(e.GetType().Name).Append('>');
            }
            return sb.Append(" playerInput.exterior=").Append(Exterior(cachedInput) ? 1 : 0)
                .Append(" onScreen: ").Append(ActiveCameras())
                .ToString();
        }

        static string ActiveCameras()
        {
            var sb = new StringBuilder();
            Camera[] cams = null;
            try { cams = UnityEngine.Object.FindObjectsOfType<Camera>(); } catch { }
            if (cams == null) return "-";
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                sb.Append(c.name).Append("(depth=").Append(c.depth.ToString("0"))
                    .Append(" mask=").Append(c.cullingMask).Append(") ");
            }
            return sb.Length == 0 ? "<none>" : sb.ToString();
        }

        string LiveState(GHPC.Player.PlayerInput pi)
        {
            var sb = new StringBuilder();
            sb.Append("pi=").Append(pi != null ? pi.gameObject.name : "-");
            sb.Append(" ext=").Append(Exterior(pi) ? 1 : 0);
            sb.Append(" canGunner=").Append(CanGunner(pi) ? 1 : 0);
            sb.Append(" seat=").Append(SeatName(SeatOf(pi)))
                .Append(" full=").Append(FullControlOf(pi) ? 1 : 0);
            var module = WeaponsModule(pi);
            var info = SafeWeapon(pi);
            sb.Append(" brain=").Append(module != null ? 1 : 0)
                .Append(" weapon=").Append(info != null ? info.Name + "/" + info.Role : "-");
            var platform = Platform(pi);
            sb.Append(" fcs=").Append(info != null && info.FCS != null ? 1 : 0)
                .Append(" platform=").Append(platform != null ? platform.Name : "-");

            var cm = CameraManager();
            var follow = FollowCam();
            var cam = MainCam();
            sb.Append(" camMgr=").Append(cm != null ? 1 : 0)
                .Append(" follow=").Append(follow != null ? 1 : 0)
                .Append(" cmd=").Append(CommanderMode() ? 1 : 0)
                .Append(" cam=").Append(cam != null ? cam.name : "-");
            // where the lens actually is when the view changes hands: the report about first person
            // sitting under the ground can only be chased down from the position itself
            if (cam != null)
                sb.Append(" camPos=").Append(cam.transform.position.ToString("F1"))
                    .Append(" camParent=").Append(cam.transform.parent != null
                        ? cam.transform.parent.name : "-")
                    .Append(" camOn=").Append(cam.gameObject.activeInHierarchy ? 1 : 0);
            var unitRoot = PlayerRoot();
            sb.Append(" root=").Append(unitRoot != null ? unitRoot.name : "-")
                .Append(" unitPos=").Append(unitRoot != null ? unitRoot.position.ToString("F1") : "-");
            sb.Append(" av=").Append(UnityEngine.Object.FindObjectsOfType<GHPC.AarVisual>().Length);
            return sb.ToString();
        }

        static bool CanGunner(GHPC.Player.PlayerInput pi)
        {
            try { return pi != null && pi.PlayerCanControlGunnerNow; }
            catch { return false; }
        }

        static readonly string[] Wanted =
        {
            "PlayerInput", "CameraManager", "BufferedCameraFollow", "AimablePlatform",
            "FireControlSystem", "CrewBrain", "Vehicle", "Unit"
        };

        // confirm at runtime which of the GHPC.* types actually have live instances, and what the
        // player vehicle root is built out of
        void Harvest()
        {
            var sb = new StringBuilder();
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type[] types;
                try { types = asms[i].GetTypes(); }
                catch { continue; }
                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                    bool hit = false;
                    for (int w = 0; w < Wanted.Length && !hit; w++)
                        hit = type.Name.IndexOf(Wanted[w], StringComparison.Ordinal) >= 0;
                    if (!hit) continue;
                    int live = 0;
                    try
                    {
                        var objs = UnityEngine.Object.FindObjectsOfType(type);
                        live = objs == null ? 0 : objs.Length;
                    }
                    catch { }
                    if (live > 0) sb.Append(type.FullName).Append('=').Append(live).Append(' ');
                }
            }
            Log("harvest live: " + (sb.Length == 0 ? "<none>" : sb.ToString()));

            var root = PlayerRoot();
            if (root == null) { Log("harvest: no player root transform"); return; }

            var list = new StringBuilder();
            var comps = root.GetComponents<MonoBehaviour>();
            for (int c = 0; c < comps.Length && c < 40; c++)
            {
                var comp = comps[c];
                if (comp == null) continue;
                list.Append(comp.GetType().FullName).Append(comp.enabled ? " " : "(off) ");
            }
            Log("harvest root " + root.name + " layer=" + root.gameObject.layer
                + " children=" + root.childCount + " comps: " + list);
        }

        // ---------------------------------------------------------------- ring UI

        void SyncRing()
        {
            int h = Screen.height;
            bool resized = h != builtScreenHeight;

            // built one to one with the pixels it will be drawn at, so RingWidth really is the line
            // thickness on screen instead of a texture-space number that shrinks with the circle
            float ringD = Mathf.Max(16f, Mathf.Clamp(eRingScale.Value, 0.005f, 0.4f) * h);
            float gunD = Mathf.Max(12f, Mathf.Clamp(eGunRingScale.Value, 0.005f, 0.4f) * h);
            float ringW = Mathf.Clamp(eRingWidth.Value, 1f, 20f);
            float gunW = Mathf.Clamp(eGunRingWidth.Value, 1f, 20f);

            if (eRing.Value && (resized || ringTexture == null || ringD != builtRingScale
                || ringW != builtRingWidth || eRingColor.Value != builtRingColor))
            {
                ringPixelSize = ringD;
                builtRingScale = ringD;
                builtRingWidth = ringW;
                builtRingColor = eRingColor.Value;
                if (ringTexture != null) UnityEngine.Object.Destroy(ringTexture);
                ringTexture = CreateRingTexture(Mathf.RoundToInt(ringD), ringW, ParseColor(builtRingColor));
                Log("circle rebuilt: " + ringTexture.width + " px, line " + ringW.ToString("0.0")
                    + " px, colour " + builtRingColor);
            }

            if (eShowGunRing.Value && (resized || gunRingTexture == null || gunD != builtGunScale
                || gunW != builtGunWidth || eGunRingColor.Value != builtGunColor))
            {
                gunRingPixelSize = gunD;
                builtGunScale = gunD;
                builtGunWidth = gunW;
                builtGunColor = eGunRingColor.Value;
                if (gunRingTexture != null) UnityEngine.Object.Destroy(gunRingTexture);
                gunRingTexture = CreateRingTexture(Mathf.RoundToInt(gunD), gunW, ParseColor(builtGunColor));
            }

            builtScreenHeight = h;
        }

        static Color ParseColor(string hex)
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString("#" + hex, out color)) color = Color.white;
            return color;
        }

        // a plain ring with a centre dot, which is what the third person aim circle looks like in the
        // game this is modelled on
        static Texture2D CreateRingTexture(int size, float widthPx, Color color)
        {
            size = Mathf.Max(12, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float r = size * 0.5f;
            float outer = r - 1f;
            float inner = outer - Mathf.Max(1f, widthPx);
            float aa = 1.1f;
            float dot = Mathf.Max(1.1f, widthPx * 0.6f);

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = Mathf.Clamp01((d - inner) / aa + 0.5f) * Mathf.Clamp01((outer - d) / aa + 0.5f);
                    if (d <= dot) a = 1f;

                    var c = color;
                    c.a *= Mathf.Clamp01(a);
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        // Where the barrel really points, in Unity screen pixels. Taken from the muzzle position the gun
        // fires from plus the platform's aim vector, projected at the same distance the centre circle is
        // hitting - so the gap to the aim circle is the turret still catching up, or the lead.
        static bool GunAimScreenPoint(Camera cam, out Vector2 screen)
        {
            screen = Vector2.zero;
            if (cam == null || !eShowGunRing.Value) return false;

            var info = SafeWeapon(cachedInput);
            var platform = Platform(cachedInput);

            Vector3 dir = Vector3.zero;
            try { if (platform != null) dir = platform.AimVector; } catch { dir = Vector3.zero; }

            var muzzle = Muzzle(info);
            if (dir.sqrMagnitude < 0.0001f)
            {
                try { if (muzzle != null) dir = muzzle.forward; } catch { dir = Vector3.zero; }
            }
            if (dir.sqrMagnitude < 0.0001f) return false;
            dir = dir.normalized;

            Vector3 origin;
            if (muzzle != null) origin = muzzle.position;
            else if (platform != null && platform.Transform != null) origin = platform.Transform.position;
            else origin = cam.transform.position;

            float dist = lastHitDistance > 1f
                ? Mathf.Clamp(lastHitDistance, 25f, 2000f)
                : Mathf.Clamp(eAimDistance.Value * 0.25f, 100f, 1000f);

            var sp = cam.WorldToScreenPoint(origin + dir * dist);
            if (sp.z <= 0.05f) return false;
            screen = new Vector2(sp.x, sp.y);
            return true;
        }

        static Transform Muzzle(GHPC.Weapons.WeaponSystemInfo info)
        {
            try
            {
                var weapon = info != null ? info.Weapon : null;
                return weapon != null ? weapon.MuzzleIdentity : null;
            }
            catch { return null; }
        }

        // In both exterior views the follow camera puts our own hull across the true screen centre, so
        // the circle - and the ray it stands for - sits a configurable distance above it. Returned in
        // Unity screen pixels (origin bottom left), which is what Camera.ScreenPointToRay wants.
        static Vector3 RingPixel()
        {
            float offset = Mathf.Clamp(eRingOffset.Value, -0.45f, 0.45f);
            return new Vector3(Screen.width * 0.5f, Screen.height * (0.5f + offset), 0f);
        }

        // Commander view draws no circle, so there is nothing to aim along: the turret follows the
        // camera direction itself, which is the true screen centre.
        static Vector3 AimPixel()
        {
            if (eHideRingCommander != null && eHideRingCommander.Value && commanderNow)
                return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            return RingPixel();
        }

        // how high the circle has to sit to clear the hull is only judgeable on screen, so let it be
        // nudged live rather than restarting the game once per guess
        void TuneRingOffset()
        {
            if (eRingOffset == null) return;

            float delta = 0f;
            try
            {
                if (Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.KeypadEquals))
                    delta = Time.unscaledDeltaTime * 0.12f;
                else if (Input.GetKey(KeyCode.KeypadMinus))
                    delta = -Time.unscaledDeltaTime * 0.12f;
            }
            catch { }
            if (delta == 0f) return;

            float next = Mathf.Clamp(eRingOffset.Value + delta, -0.45f, 0.45f);
            if (Mathf.Abs(next - eRingOffset.Value) < 0.0005f) return;
            eRingOffset.Value = next;

            tuneSaveTimer -= Time.unscaledDeltaTime;
            if (tuneSaveTimer > 0f) return;
            tuneSaveTimer = 1f;
            Log("RingOffsetY = " + next.ToString("0.000"));
            try { cat.SaveToFile(false); } catch (Exception e) { Log("saving the offset threw: " + e.Message); }
        }

        // ---------------------------------------------------------------- drawing

        // the game draws its own third person reticle through OnGUI, so the same channel is the one
        // proven to reach the screen here; a canvas built during OnInitializeMelon does not survive
        // the mission scene load, which is why the circle was invisible
        public override void OnGUI()
        {
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            DrawNotice();
            if (activeNow && eRing.Value && ringTexture != null
                && !(eHideRingCommander.Value && commanderNow))
            {
                var px = RingPixel();
                GUI.DrawTexture(new Rect(px.x - ringPixelSize * 0.5f,
                    Screen.height - px.y - ringPixelSize * 0.5f, ringPixelSize, ringPixelSize), ringTexture);
            }
            if (activeNow && gunRingTexture != null)
            {
                var cam = MainCam();
                Vector2 sp;
                if (GunAimScreenPoint(cam, out sp)
                    && sp.x > -40f && sp.y > -40f
                    && sp.x < Screen.width + 40f && sp.y < Screen.height + 40f)
                {
                    GUI.DrawTexture(new Rect(sp.x - gunRingPixelSize * 0.5f,
                        Screen.height - sp.y - gunRingPixelSize * 0.5f,
                        gunRingPixelSize, gunRingPixelSize), gunRingTexture);
                }
            }
            GUI.matrix = matrix;
        }

        static GUIStyle noticeStyle;

        // the toggle has to announce itself: a circle that silently stops being drawn reads as a crash
        static void DrawNotice()
        {
            if (noticeTimer <= 0f || string.IsNullOrEmpty(noticeText)) return;
            if (noticeStyle == null)
            {
                noticeStyle = new GUIStyle(GUI.skin.label);
                noticeStyle.fontSize = 16;
                noticeStyle.alignment = TextAnchor.MiddleCenter;
            }

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(noticeTimer * 1.5f));
            GUI.Label(new Rect(0f, Screen.height * 0.07f, Screen.width, 28f), noticeText, noticeStyle);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- plumbing

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
                log = new StreamWriter(Path.Combine(dir, "SlewReticle.debug.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
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
