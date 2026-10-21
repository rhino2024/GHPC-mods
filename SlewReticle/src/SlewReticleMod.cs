using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SlewReticle.SlewReticleMod), "Slew Reticle", "1.0.0", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace SlewReticle
{
    // War-Thunder style third person, automatic: whenever the game is in its exterior camera, a
    // fixed circle sits in the middle of the screen and the turret is slewed to whatever that circle
    // is over. Leaving the exterior view (or binoculars / free cam / the AAR) switches it off again.
    //
    // The whole thing runs through the live gameplay classes - GHPC.Player.PlayerInput,
    // GHPC.Camera.CameraManager and GHPC.Crew.CrewBrainWeaponsModule. The global PlayerControl /
    // SlewableTurret / BufferedCameraFollow types an IL dump points at are obsolete stubs with zero
    // instances in a running mission.
    public sealed class SlewReticleMod : MelonMod
    {
        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<KeyCode> eToggle;
        static MelonPreferences_Entry<bool> eSlew, eRing, eDebugLog;
        static MelonPreferences_Entry<float> eRingScale, eRingWidth, eAimDistance;
        static MelonPreferences_Entry<float> eRingOffset;
        static MelonPreferences_Entry<string> eRingColor;

        static bool featureOn = true;
        static bool activeNow;
        static Texture2D ringTexture;
        static float ringPixelSize;
        static string ringColorApplied;
        static int ringScreenHeight;
        static StreamWriter log;

        static Vector3 lastAimDir;
        static float reportTimer;
        static string lastHitLabel = "-";
        static GHPC.Player.PlayerInput cachedInput;
        static bool inMission;
        static bool harvested;
        static string pendingKeys = "";
        static string lastView = "";
        static float viewTimer;
        static float tuneSaveTimer;

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("SlewReticle", "Slew Reticle");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.G, "Turn the whole feature on or off; while on it follows the exterior view by itself");
            eSlew = cat.CreateEntry<bool>("AutoSlew", true, "Slew the turret to whatever the screen centre ray hits");
            eRing = cat.CreateEntry<bool>("ShowRing", true, "Draw the fixed screen-centre circle");
            eRingScale = cat.CreateEntry<float>("RingScale", 0.055f, "Circle diameter as a fraction of screen height");
            eRingWidth = cat.CreateEntry<float>("RingWidth", 3f, "Circle line width in pixels");
            eRingColor = cat.CreateEntry<string>("RingColor", "4DE673", "Circle colour, RRGGBB hex");
            eAimDistance = cat.CreateEntry<float>("AimDistance", 2000f, "How far the centre ray reaches when it hits nothing");
            eRingOffset = cat.CreateEntry<float>("RingOffsetY", 0.18f, "Circle height above the true screen centre, as a fraction of screen height; the aim ray leaves from the circle. Nudge it live with Keypad + / -");
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
            if (Input.GetKeyDown(eToggle.Value))
            {
                featureOn = !featureOn;
                Log("feature " + (featureOn ? "enabled" : "disabled"));
            }

            var pi = featureOn ? FindInput() : null;
            SetActive(pi);

            if (!activeNow) return;
            SyncRing();
            if (eSlew.Value) Slew(pi);
            if (eDebugLog.Value) Report(pi);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            SetActive(null);
            cachedInput = null;
            harvested = false;
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
            if (want) { lastAimDir = Vector3.zero; reportTimer = 0f; SyncRing(); }
            Log("slew " + (want ? "ON" : "off") + " | " + LiveState(pi)
                + (want ? " ring=" + (ringTexture != null ? ringTexture.width + "px" : "none")
                    + " offsetY=" + eRingOffset.Value : ""));
        }

        void Slew(GHPC.Player.PlayerInput pi)
        {
            var cam = MainCam();
            if (cam == null || !cam.isActiveAndEnabled) return;

            var centre = RingPixel();
            var ray = cam.ScreenPointToRay(centre);
            float far = Mathf.Clamp(eAimDistance.Value, 50f, 4000f);

            RaycastHit hit;
            Vector3 point;
            if (Physics.Raycast(ray, out hit, far) && !OwnVehicle(hit.transform))
            {
                point = hit.point;
                lastHitLabel = hit.transform.name + " " + hit.distance.ToString("0") + "m";
            }
            else
            {
                point = cam.transform.position + ray.direction * far;
                lastHitLabel = "sky";
            }

            var module = WeaponsModule(pi);
            var info = SafeWeapon(pi);
            if (module == null || info == null) return;

            lastAimDir = (point - cam.transform.position).normalized;
            try { module.SetAimPosition(info.Role, point); }
            catch (Exception e) { Log("SetAimPosition threw: " + e.Message); }
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
            Log("slew: gun-vs-centre error " + err.ToString("0.0") + " deg, centre hits " + lastHitLabel);
        }

        // ---------------------------------------------------------------- live object access

        GHPC.Player.PlayerInput FindInput()
        {
            if (cachedInput != null && cachedInput.gameObject != null) return cachedInput;
            cachedInput = null;

            try { cachedInput = GHPC.Player.PlayerInput.Instance; }
            catch (Exception e) { Log("PlayerInput.Instance threw: " + e.Message); }
            if (cachedInput != null) return cachedInput;

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
                .Append(" cam=").Append(cam != null ? cam.name : "-")
                .Append(" root=").Append(PlayerRoot() != null ? PlayerRoot().name : "-");
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
            if (!eRing.Value) return;
            if (ringColorApplied != eRingColor.Value || ringScreenHeight != Screen.height)
            {
                ringColorApplied = eRingColor.Value;
                ringScreenHeight = Screen.height;
                RebuildRing();
            }
        }

        void RebuildRing()
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString("#" + eRingColor.Value, out color)) color = Color.green;
            float width = Mathf.Clamp(eRingWidth.Value, 1f, 20f);

            if (ringTexture != null) UnityEngine.Object.Destroy(ringTexture);
            ringTexture = CreateRingTexture(256, width, color);
            Log("ring texture rebuilt: " + ringTexture.width + " px, colour " + eRingColor.Value);
        }

        static Texture2D CreateRingTexture(int size, float widthPx, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;

            float r = size * 0.5f;
            float outer = r - 2f;
            float inner = outer - widthPx;
            float aa = 1.4f;
            float tickLength = outer * 0.34f;
            float tickHalf = widthPx * 0.6f;
            float dotRadius = Mathf.Max(1.6f, widthPx * 0.7f);

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = Mathf.Clamp01((d - inner) / aa + 0.5f) * Mathf.Clamp01((outer - d) / aa + 0.5f);

                    // four inward ticks on the axes, like a gunner's reticle
                    float angle = Mathf.Atan2(dy, dx);
                    float nearestAxis = Mathf.Round(angle / (Mathf.PI * 0.5f)) * (Mathf.PI * 0.5f);
                    float arcOffset = Mathf.Abs(angle - nearestAxis) * d;
                    if (arcOffset <= tickHalf && d <= inner && d >= inner - tickLength) a = 1f;
                    if (d <= dotRadius) a = 1f;

                    var c = color;
                    c.a *= Mathf.Clamp01(a);
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        void ResizeRing()
        {
            float px = Mathf.Clamp(eRingScale.Value, 0.005f, 0.4f) * Screen.height;
            ringPixelSize = Mathf.Max(16f, px);
        }

        // In both exterior views the follow camera puts our own hull across the true screen centre, so
        // the circle - and the ray it stands for - sits a configurable distance above it. Returned in
        // Unity screen pixels (origin bottom left), which is what Camera.ScreenPointToRay wants.
        static Vector3 RingPixel()
        {
            float offset = Mathf.Clamp(eRingOffset.Value, -0.45f, 0.45f);
            return new Vector3(Screen.width * 0.5f, Screen.height * (0.5f + offset), 0f);
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
            if (!activeNow || !eRing.Value || ringTexture == null) return;

            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            ResizeRing();
            var px = RingPixel();
            GUI.DrawTexture(new Rect(px.x - ringPixelSize * 0.5f,
                Screen.height - px.y - ringPixelSize * 0.5f, ringPixelSize, ringPixelSize), ringTexture);
            GUI.matrix = matrix;
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
