using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GHPC;
using ShellInfo = GHPC.Weapons.ShotInfo;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HitCam
{
    internal sealed class ReplaySession
    {
        const int MaxCaptionLines = 4;

        static readonly MethodInfo CreateShotTrace =
            AccessTools.Method(typeof(AarController), "createShotTrace");
        static readonly FieldInfo ShotTraceList =
            AccessTools.Field(typeof(AarController), "_shotTraces");
        static readonly FieldInfo MainShotTraceList =
            AccessTools.Field(typeof(AarController), "_mainShotTraces");
        static readonly FieldInfo StartShotTrace =
            AccessTools.Field(typeof(AarController), "_startShotTrace");
        static readonly FieldInfo VisualRenderers =
            AccessTools.Field(typeof(AarVisual), "_renderers");
        static readonly MethodInfo VisualMaterials =
            AccessTools.Method(typeof(AarVisual), "updateMaterials");

        sealed class VisualState
        {
            public AarVisual Visual;
            public bool Highlighted;
        }

        readonly ShellInfo shot;
        readonly Transform pivot;
        readonly GameObject cameraRoot;
        readonly Camera replayCamera;
        readonly GameObject xrayRoot;
        readonly Camera xrayCamera;
        readonly RenderTexture texture;
        GameObject uiRoot;

        readonly List<VisualState> states = new List<VisualState>();
        readonly List<GameObject> suppressedTraces = new List<GameObject>();
        readonly List<GameObject> createdTraces = new List<GameObject>();

        Vector3 centre;
        Bounds interiorBounds;
        float lastBeat;
        int renderFailures;
        int frames;
        float worstRenderMs;
        float radius = 14f;
        float aimUp = 1.2f;
        float victimExtent = 8f;
        float yaw;
        float elapsed;
        bool tracesCreated;
        bool visualsApplied;

        ReplaySession(ShellInfo target, IUnit victim)
        {
            shot = target;
            pivot = victim.transform;

            texture = new RenderTexture(HitCamMod.WindowWidth, HitCamMod.WindowHeight, 24);
            int live = LiveViewMask();

            // exactly the two-pass stack the game builds for its own AAR: the world pass draws
            // everything the player can already see, the second pass clears only the depth buffer
            // and paints the AAR layer over it. That is why the AAR looks see-through without the
            // hull ever becoming transparent - and why no exterior renderer has to be touched
            cameraRoot = new GameObject("HitCam_ReplayCamera");
            replayCamera = AddCamera(cameraRoot, CameraClearFlags.SolidColor, live, 100f);

            xrayRoot = new GameObject("HitCam_XrayCamera");
            xrayRoot.transform.SetParent(cameraRoot.transform, false);
            xrayCamera = AddCamera(xrayRoot, CameraClearFlags.Depth, InteriorMask(), 101f);

            BuildVictimFraming(victim);
            AimCamera();
            SweepLiveTraces();
            CreateTraces(victim);
            if (!HitCamMod.ShowFlightPath) SuppressFlightPath();
            if (HitCamMod.ShowInterior) CollectVisuals(victim, live);
            // lit once for the whole window rather than twice per frame
            ApplyVisuals();
            BuildUi();
        }

        Camera AddCamera(GameObject root, CameraClearFlags clear, int mask, float depth)
        {
            var camera = root.AddComponent<Camera>();
            camera.clearFlags = clear;
            camera.backgroundColor = Color.black;
            camera.orthographic = false;
            camera.fieldOfView = HitCamMod.FieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            camera.depth = depth;
            camera.cullingMask = mask;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.targetTexture = texture;
            // never enabled: the window is drawn by our own Render() calls, so the game's camera
            // stack and its timing are untouched
            camera.enabled = false;
            return camera;
        }

        public static ReplaySession TryStart(ShellInfo shot, IUnit victim)
        {
            if (shot == null || victim == null || victim.transform == null) return null;
            if (AarController.Instance == null || CreateShotTrace == null)
            {
                HitCamMod.Log("AarController.createShotTrace unavailable");
                return null;
            }

            try { return new ReplaySession(shot, victim); }
            catch (Exception e)
            {
                HitCamMod.Log("session threw: " + e);
                return null;
            }
        }

        void BuildVictimFraming(IUnit victim)
        {
            centre = pivot.position;
            yaw = Mathf.Atan2(shot.StartPosition.x - centre.x, shot.StartPosition.z - centre.z) * Mathf.Rad2Deg;

            // colliders track the real hull; renderer bounds blow up with muzzle flash
            // particles and the oversized quads mod vehicles ship with
            var bounds = new Bounds(centre, Vector3.zero);
            bool measured = false;

            var colliders = victim.transform.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].bounds.size.magnitude > 60f) continue;
                bounds.Encapsulate(colliders[i].bounds);
                measured = true;
            }
            if (!measured)
            {
                var renderers = victim.transform.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (!renderers[i].enabled || renderers[i].bounds.size.magnitude > 30f) continue;
                    bounds.Encapsulate(renderers[i].bounds);
                    measured = true;
                }
            }
            if (!measured) bounds.size = new Vector3(9f, 4f, 9f);

            // generous on top and bottom: the follow roots the turret art and the crew hang off
            // sit above the hull collider envelope
            bounds.Expand(new Vector3(2.5f, 4f, 2.5f));
            interiorBounds = bounds;

            victimExtent = Mathf.Max(bounds.size.x, bounds.size.z);
            radius = Mathf.Clamp(victimExtent * 1.9f * HitCamMod.DistanceScale, 6f, 40f);
            aimUp = Mathf.Clamp(bounds.center.y - pivot.position.y, 0.4f, 3f);
        }

        // The AAR interior is entirely AarVisual driven (measured in the real AAR: 0 AarModel in
        // scene and in the assets, 386 AarVisual). Highlighting one does the whole job the way the
        // game does it: it enables its renderers, swaps in the coloured AarMaterial, and moves the
        // object to the AAR layer. Outside the AAR the PreserveLayerUntilAar guard makes that last
        // step a no-op, so the pieces stay on their raycast-only layer - invisible to every camera
        // in the game, visible to ours.
        void CollectVisuals(IUnit victim, int liveMask)
        {
            var roots = InteriorRoots(victim);
            var seen = new HashSet<AarVisual>();
            interiorLayers = (1 << XrayLayer()) | (1 << AarLayer());

            AddVisuals(victim.transform.GetComponentsInChildren<AarVisual>(true), seen, liveMask);
            for (int i = 0; i < roots.Count; i++)
                AddVisuals(roots[i].GetComponentsInChildren<AarVisual>(true), seen, liveMask);

            int fromHierarchy = states.Count;
            var proximity = NearbyVisuals();
            AddVisuals(proximity, seen, liveMask);
            int fromProximity = states.Count - fromHierarchy;

            int revealed = RevealHiddenLayers(roots, liveMask);

            xrayCamera.cullingMask |= interiorLayers;
            xrayCamera.cullingMask &= ~liveMask;

            var layers = new StringBuilder();
            var listed = new HashSet<int>();
            int mask = interiorLayers;
            for (int bit = 0; bit < 32; bit++)
                if ((mask & (1 << bit)) != 0 && listed.Add(bit)) layers.Append(bit).Append(' ');

            var rootNames = new StringBuilder();
            for (int i = 0; i < roots.Count && i < 4; i++)
                rootNames.Append(roots[i].name).Append(' ');

            HitCamMod.Log("xray: " + states.Count + " of " + candidates + " AarVisual highlighted ("
                + fromHierarchy + " by parent, " + fromProximity + " by position, " + exteriorSkips
                + " exterior skipped), " + movedLayers + " renderers moved to layer " + XrayLayer()
                + ", " + stuckLayers + " kept on their layer (collider carrier), "
                + revealed + " already-lit renderers on hidden layers, from "
                + roots.Count + " roots [" + rootNames + "], xray pass on layers "
                + layers + " (mask " + xrayCamera.cullingMask + ")");

            if (!probedUnits.Contains(victim))
            {
                probedUnits.Add(victim);
                HitCamMod.Log("xray box " + interiorBounds + ": "
                    + AarProbe.DescribeVisuals(proximity, 3));
                ProbeScene(victim, shot, roots);
            }
        }

        // the interior and the crew do not live under the unit transform (measured: only 2
        // AarVisual there), so anything sitting inside the struck hull has to be found by asking
        // where it is rather than who owns it
        AarVisual[] NearbyVisuals()
        {
            var all = UnityEngine.Object.FindObjectsOfType<AarVisual>();
            var close = new List<AarVisual>();
            for (int i = 0; i < all.Length; i++)
            {
                var visual = all[i];
                if (visual == null) continue;
                if (interiorBounds.Contains(visual.transform.position)) close.Add(visual);
            }
            return close.ToArray();
        }

        // the crew is not AarVisual art: their bodies stay enabled the whole battle, they simply
        // sit on a layer the player camera refuses to render, which is why nobody ever sees them
        // through the hull. Pointing our second pass at those layers is all it takes.
        int RevealHiddenLayers(List<Transform> roots, int liveMask)
        {
            int count = 0;
            for (int i = 0; i < roots.Count; i++)
            {
                var renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var renderer = renderers[r];
                    if (renderer == null || !renderer.enabled) continue;
                    if (!renderer.gameObject.activeInHierarchy) continue;
                    if (renderer is ParticleSystemRenderer) continue;
                    int layer = renderer.gameObject.layer;
                    if ((liveMask & (1 << layer)) != 0) continue;
                    interiorLayers |= 1 << layer;
                    count++;
                }
            }
            return count;
        }

        void AddVisuals(AarVisual[] found, HashSet<AarVisual> seen, int liveMask)
        {
            for (int i = 0; i < found.Length; i++)
            {
                var visual = found[i];
                if (visual == null || seen.Contains(visual)) continue;
                seen.Add(visual);
                candidates++;

                var renderers = RenderersOf(visual);

                // the giveaway for exterior art is not its layer but whether anything of it is
                // drawn right now: dormant interior art (measured: the driver's body sits on
                // layer 0) draws nothing until we light it, so it can never be the outer hull
                if ((liveMask & (1 << visual.gameObject.layer)) != 0 && CountOn(renderers) > 0)
                {
                    exteriorSkips++;
                    continue;
                }

                for (int r = 0; r < renderers.Length; r++)
                    if (renderers[r] != null) interiorLayers |= 1 << renderers[r].gameObject.layer;

                states.Add(new VisualState { Visual = visual, Highlighted = visual.Highlighted });
                PushToXrayLayer(renderers);
            }
        }

        // the overlay pass only clears depth, so whatever it renders paints over the hull - but it
        // can only render layers the player camera does not use. Art already sitting on a player
        // visible layer has to be stepped aside onto Xray Only for the length of the window and
        // moved back on close; objects carrying their own collider stay put, moving those would
        // silently change what incoming rounds can hit
        void PushToXrayLayer(Renderer[] renderers)
        {
            int target = XrayLayer();
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null || !moved.Add(renderer)) continue;
                var gameObject = renderer.gameObject;
                if (gameObject.layer == target) continue;
                // an object with its own collider is part of the hit model: moving it would change
                // what incoming rounds can strike, so it has to stay on its layer and take its
                // chances with depth occlusion instead
                if (gameObject.GetComponent<Collider>() != null)
                {
                    stuckLayers++;
                    continue;
                }
                layerMoves.Add(new LayerMove { Object = gameObject, Layer = gameObject.layer });
                gameObject.layer = target;
                movedLayers++;
            }
        }

        void RestoreLayers()
        {
            for (int i = layerMoves.Count - 1; i >= 0; i--)
            {
                var move = layerMoves[i];
                if (move.Object != null) move.Object.layer = move.Layer;
            }
            layerMoves.Clear();
            moved.Clear();
        }

        static int CountOn(Renderer[] renderers)
        {
            int n = 0;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled) n++;
            return n;
        }

        sealed class LayerMove
        {
            public GameObject Object;
            public int Layer;
        }

        readonly List<LayerMove> layerMoves = new List<LayerMove>();
        readonly HashSet<Renderer> moved = new HashSet<Renderer>();
        int movedLayers;
        int stuckLayers;
        int exteriorSkips;

        int interiorLayers;
        int candidates;

        static Renderer[] RenderersOf(AarVisual visual)
        {
            var list = VisualRenderers != null ? VisualRenderers.GetValue(visual) as System.Collections.IEnumerable : null;
            var found = new List<Renderer>();
            if (list != null)
                foreach (var item in list)
                {
                    var renderer = item as Renderer;
                    if (renderer != null) found.Add(renderer);
                }
            if (found.Count > 0) return found.ToArray();
            return visual.GetComponentsInChildren<Renderer>(true);
        }

        // the interior art and the crew do NOT live under the unit's transform (measured: only 2
        // AarVisual there, both machine gun parts). They sit under the per-part follow roots,
        // HULL FOLLOW and TURRET FOLLOW, which the armour handles of the shot hang inside
        List<Transform> InteriorRoots(IUnit victim)
        {
            var roots = new List<Transform>();
            var frames = shot.AllShotFrames;
            if (frames != null)
                for (int i = 0; i < frames.Count; i++)
                    AddRoot(roots, frames[i].HitArmor);
            return roots;
        }

        static void AddRoot(List<Transform> roots, IArmor armor)
        {
            if (armor == null) return;
            var root = FollowRootOf(armor.Transform);
            if (root != null && !roots.Contains(root)) roots.Add(root);
        }

        static Transform FollowRootOf(Transform t)
        {
            if (t == null) return null;
            Transform follow = null;
            Transform aar = null;
            var walk = t;
            int hops = 0;
            while (walk != null && hops < 14)
            {
                // climb all the way: the outermost follow root carries both the AAR subtree and
                // the crew, which live as siblings rather than inside Hull AAR
                if (IsFollowName(walk.name)) follow = walk;
                else if (aar == null && IsAarName(walk.name)) aar = walk;
                walk = walk.parent;
                hops++;
            }
            if (follow != null) return follow;
            if (aar != null) return aar;
            return t;
        }

        static bool IsFollowName(string name)
        {
            if (name == null) return false;
            return name.EndsWith("FOLLOW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Follow", StringComparison.Ordinal);
        }

        static bool IsAarName(string name)
        {
            if (name == null) return false;
            return name.IndexOf("AAR", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Xray", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ApplyVisuals()
        {
            if (visualsApplied) return;
            // flagged first: a throwing setter partway through must still get the restore pass
            visualsApplied = true;
            int failed = 0;
            for (int i = 0; i < states.Count; i++)
            {
                var visual = states[i].Visual;
                if (visual == null || visual.Highlighted) continue;
                try
                {
                    // the setter itself runs UpdateVisibility, which reads the player camera for
                    // XrayOnly pieces - that is the one thing that can throw here
                    visual.Highlighted = true;
                }
                catch (Exception e)
                {
                    if (failed++ == 0) HitCamMod.Log("highlight threw: " + e.Message);
                }
            }
            if (failed > 0) HitCamMod.Log("highlight: " + failed + " of " + states.Count + " refused");
        }

        void RestoreVisuals()
        {
            if (!visualsApplied) return;
            visualsApplied = false;

            for (int i = states.Count - 1; i >= 0; i--)
            {
                var state = states[i];
                var visual = state.Visual;
                if (visual == null || visual.Highlighted == state.Highlighted) continue;
                visual.Highlighted = state.Highlighted;
                // UpdateVisibility only refreshes materials while the piece is enabled, so the
                // colour swap needs its own nudge on the way back out
                if (VisualMaterials != null)
                {
                    try { VisualMaterials.Invoke(visual, null); }
                    catch { }
                }
            }
        }

        static readonly HashSet<IUnit> probedUnits = new HashSet<IUnit>();

        static void ProbeScene(IUnit victim, ShellInfo shot, List<Transform> roots)
        {
            var t = victim.transform;
            HitCamMod.Log("probe unit: " + t.name + " root=" + t.root.name + " kids=" + t.childCount
                + " aarModel=" + t.GetComponentsInChildren<AarModel>(true).Length
                + " aarVisual=" + t.GetComponentsInChildren<AarVisual>(true).Length
                + " renderer=" + t.GetComponentsInChildren<Renderer>(true).Length);

            var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
            var masks = new StringBuilder();
            for (int i = 0; i < cams.Length && i < 10; i++)
                masks.Append(cams[i].name).Append(":m").Append(cams[i].cullingMask)
                    .Append(":rt").Append(cams[i].targetTexture != null ? 1 : 0).Append(' ');
            HitCamMod.Log("probe cameras: " + masks);

            var visuals = victim.transform.GetComponentsInChildren<AarVisual>(true);
            HitCamMod.Log("probe unit visuals: " + AarProbe.DescribeVisuals(visuals, 3));

            for (int i = 0; i < roots.Count && i < 6; i++)
                HitCamMod.Log("probe root path: " + Path(roots[i]));

            for (int i = 0; i < roots.Count && i < 6; i++)
            {
                var rootVisuals = roots[i].GetComponentsInChildren<AarVisual>(true);
                var renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                int on = 0;
                int sleeping = 0;
                var hist = new StringBuilder();
                var perLayer = new Dictionary<int, int>();
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null) continue;
                    if (renderers[r].enabled)
                    {
                        // an enabled renderer under an inactive ancestor still draws nothing, so
                        // the two cases need opposite fixes
                        if (renderers[r].gameObject.activeInHierarchy) on++;
                        else sleeping++;
                    }
                    int layer = renderers[r].gameObject.layer;
                    int n;
                    perLayer.TryGetValue(layer, out n);
                    perLayer[layer] = n + 1;
                }
                foreach (var pair in perLayer)
                    hist.Append(pair.Key).Append('x').Append(pair.Value).Append(' ');

                HitCamMod.Log("probe armor root: " + Path(roots[i]) + " visuals="
                    + AarProbe.DescribeVisuals(rootVisuals, 2) + " [" + rootVisuals.Length
                    + " aarVisual, " + renderers.Length + " renderers, " + on + " live, "
                    + sleeping + " enabled but inactive, layers " + hist + "]");
            }
        }

        static string Path(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
            var walk = t.parent;
            int hops = 0;
            while (walk != null && hops < 20)
            {
                sb.Insert(0, walk.name + "/");
                walk = walk.parent;
                hops++;
            }
            return sb.ToString();
        }

        // diagnostics are per vehicle type, but the references belong to the old mission
        public static void ForgetProbes()
        {
            probedUnits.Clear();
        }

        static Camera MainViewCamera()
        {
            Camera best = null;
            int bestBits = -1;
            var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null || cam.targetTexture != null) continue;
                if (cam.name.StartsWith("HitCam")) continue;
                int bits = BitCount(cam.cullingMask);
                if (bits > bestBits) { bestBits = bits; best = cam; }
            }
            return best;
        }

        static int BitCount(int mask)
        {
            int n = 0;
            while (mask != 0) { mask &= mask - 1; n++; }
            return n;
        }

        static int LiveViewMask()
        {
            var cam = MainViewCamera();
            return cam != null ? cam.cullingMask : ~0;
        }

        static int AarLayer()
        {
            var manager = ConstantsAndInfoManager.Instance;
            return manager != null && manager.AarLayer > 0 ? manager.AarLayer : 15;
        }

        static int XrayLayer()
        {
            var manager = ConstantsAndInfoManager.Instance;
            return manager != null ? manager.XrayOnlyLayer : 21;
        }

        // before any AarVisual is known: the layers no game camera renders, where the AAR art
        // waits during battle, plus the one the shot traces get pushed onto
        static int InteriorMask()
        {
            int raycast = LayerMask.NameToLayer("Raycast Only");
            return (1 << XrayLayer()) | (1 << AarLayer()) | (raycast >= 0 ? 1 << raycast : 1 << 8);
        }

        void CreateTraces(IUnit victim)
        {
            // the field hands out the game's live List, so only the count survives the invoke
            int before = TraceCount();
            try
            {
                CreateShotTrace.Invoke(AarController.Instance, new object[] { shot, false });
                tracesCreated = true;
            }
            catch (Exception e)
            {
                HitCamMod.Log("createShotTrace failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
                return;
            }

            var live = ReadTraceList();
            if (live == null) return;
            for (int i = before; i < live.Count; i++)
                if (live[i] != null) createdTraces.Add(live[i]);

            var names = new StringBuilder();
            for (int i = 0; i < createdTraces.Count && i < 6; i++)
                names.Append(createdTraces[i].name).Append(' ');
            HitCamMod.Log("traces added: " + createdTraces.Count + " [" + names + "]"
                + (createdTraces.Count > 0 ? " layer " + createdTraces[0].layer : ""));
            MoveToXrayLayer();
        }

        // the traces land on the AAR layer, which the player's camera renders, so the world view
        // would show stray lines for the length of the window. Xray Only is excluded there and is
        // part of our second pass, so only the window keeps them.
        void MoveToXrayLayer()
        {
            int layer = XrayLayer();
            for (int i = 0; i < createdTraces.Count; i++)
            {
                var go = createdTraces[i];
                if (go == null) continue;
                // shot traces carry only mesh filters and renderers, no colliders, so moving the
                // whole object is safe; the rings ShotTraceVisual.Start creates afterwards copy
                // this layer from their parent, so they come along too
                go.layer = layer;
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    if (renderers[r] != null) renderers[r].gameObject.layer = layer;
            }
        }

        int TraceCount()
        {
            var list = ReadTraceList();
            return list == null ? 0 : list.Count;
        }

        // an earlier build leaked every trace it made, so old flight lines piled up in the
        // live scene; during play only we create traces, so emptying the lists is safe
        static void SweepLiveTraces()
        {
            if (AarController.InAar) return;
            var all = ReadTraceList(ShotTraceList);
            if (all == null) return;

            int swept = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null)
                {
                    UnityEngine.Object.Destroy(all[i]);
                    swept++;
                }

            all.Clear();
            var main = ReadTraceList(MainShotTraceList);
            if (main != null) main.Clear();
            if (StartShotTrace != null && AarController.Instance != null)
                StartShotTrace.SetValue(AarController.Instance, null);

            if (swept > 0) HitCamMod.Log("swept " + swept + " leftover traces from the live scene");
        }

        static List<GameObject> ReadTraceList(FieldInfo listField)
        {
            if (listField == null || AarController.Instance == null) return null;
            return listField.GetValue(AarController.Instance) as List<GameObject>;
        }

        List<GameObject> ReadTraceList()
        {
            return ReadTraceList(ShotTraceList);
        }

        // createShotTrace only names its gun-to-target line "startLegTrace" when the first
        // recorded frame is already more than one trace step away; for the shots we replay it
        // instead stretches the FIRST "hitLegTrace" from the muzzle to the target, so the
        // giveaway is the leg length, not the name
        void SuppressFlightPath()
        {
            float cutoff = HitCamMod.FlightLegMeters;
            for (int i = 0; i < createdTraces.Count; i++)
            {
                var go = createdTraces[i];
                if (go == null) continue;
                var visual = go.GetComponent<ShotTraceVisual>();
                if (visual == null || visual.EndPoints == null || visual.EndPoints.Length == 0) continue;
                if (Vector3.Distance(visual.StartPoint, visual.EndPoints[0]) < cutoff) continue;
                go.SetActive(false);
                suppressedTraces.Add(go);
            }
            HitCamMod.Log("flight line: " + suppressedTraces.Count + " of " + createdTraces.Count
                + " traces hidden, cutoff " + cutoff.ToString("0") + "m");
        }

        // not AarController.ClearShotTraces(): that also walks every AarModel in the mission,
        // which is far too much to disturb during live play
        void RemoveOwnTraces()
        {
            for (int i = 0; i < createdTraces.Count; i++)
                if (createdTraces[i] != null) UnityEngine.Object.Destroy(createdTraces[i]);

            TrimDestroyed(ShotTraceList);
            TrimDestroyed(MainShotTraceList);

            if (StartShotTrace != null && AarController.Instance != null
                && StartShotTrace.GetValue(AarController.Instance) == null)
                StartShotTrace.SetValue(AarController.Instance, null);
        }

        static void TrimDestroyed(FieldInfo listField)
        {
            if (listField == null || AarController.Instance == null) return;
            var list = listField.GetValue(AarController.Instance) as List<GameObject>;
            if (list == null) return;
            list.RemoveAll(IsDestroyed);
        }

        static bool IsDestroyed(GameObject go)
        {
            return go == null;
        }

        void BuildUi()
        {
            uiRoot = new GameObject("HitCam_Window");
            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;

            int margin = HitCamMod.Margin;
            int width = HitCamMod.WindowWidth;
            int height = HitCamMod.WindowHeight;

            var picture = AddGraphic<RawImage>(margin, margin, width, height);
            picture.texture = texture;
            picture.color = Color.white;
            picture.raycastTarget = false;

            if (!HitCamMod.ShowInfoText) return;

            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (font == null) return;

            var caption = BuildCaption();
            var lines = caption.Split('\n');
            var text = AddGraphic<Text>(margin, margin + height + 4, width, 20 * lines.Length);
            text.font = font;
            text.text = caption;
            text.alignment = TextAnchor.UpperCenter;
            text.fontSize = 15;
            text.lineSpacing = 1.15f;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = 15;
            HitCamMod.Log("caption: " + caption.Replace("\n", "  //  "));
        }

        T AddGraphic<T>(int marginX, int offsetY, int width, int height) where T : Graphic
        {
            var go = new GameObject(typeof(T).Name + "_HitCam");
            go.transform.SetParent(uiRoot.transform, false);
            var graphic = go.AddComponent<T>();
            var rect = graphic.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-marginX, -offsetY);
            rect.sizeDelta = new Vector2(width, height);
            return graphic;
        }

        // XUnity AutoTranslator hooks UnityEngine.UI.Text.set_text, so any line the game itself
        // uses comes out in the player's language, but words we invent ("DAMAGE", "pen") are not in
        // its registry and stay English. Hence: numbers and the ammo designator only, plus the
        // game's own story lines - the kill / damage result is already stated by those lines.
        string BuildCaption()
        {
            string ammo = shot.TypeInfo != null ? shot.TypeInfo.Name : "SHELL";
            string head = ammo + "   " + shot.Distance.ToString("0") + " m   " + shot.ActualPen.ToString("0") + " mm";

            string modules = DescribeDamage();
            return modules.Length == 0 ? head : head + "\n" + modules;
        }

        // LiveRound.AddEvent feeds every shot narration line into ShotStory, whose builder the
        // game itself prints in the AAR: those lines already name the armour section and the
        // module the round struck, so replaying them borrows the game's vocabulary verbatim.
        string DescribeDamage()
        {
            var labels = new List<string>();
            var seen = new HashSet<string>();

            string story = shot.Story != null ? shot.Story.FinalString : null;
            if (!string.IsNullOrEmpty(story))
            {
                var lines = story.Split('\n');
                // newest events first: "Penetrated …" / "Hit …" end the list, launch-time
                // fuze chatter starts it
                for (int i = lines.Length - 1; i >= 0 && labels.Count < MaxCaptionLines; i--)
                    AddLabel(labels, seen, lines[i].Trim());
                labels.Reverse();
            }

            if (labels.Count == 0)
            {
                var frames = shot.AllShotFrames;
                if (frames == null) return string.Empty;
                for (int i = 0; i < frames.Count && labels.Count < MaxCaptionLines; i++)
                {
                    var armor = frames[i].HitArmor;
                    if (armor != null) AddLabel(labels, seen, armor.Name);
                }
            }

            if (labels.Count == 0) return string.Empty;
            return string.Join("\n", labels.ToArray());
        }

        static void AddLabel(List<string> labels, HashSet<string> seen, string label)
        {
            if (label == null || label.Length == 0) return;
            if (labels.Count >= MaxCaptionLines || !seen.Add(label)) return;
            labels.Add(label);
        }

        // returns true once the window is done and should be torn down
        // the shot terminates a beat before the game cuts to the AAR screen, so aborting on
        // InAar used to kill the window before its first frame was ever on screen
        public bool Update(float deltaTime)
        {
            if (pivot == null) return true;

            elapsed += deltaTime;
            // once the game cuts to its own AAR the after-effects are on the full screen anyway,
            // and the AAR begins rewriting the very AarVisual state we hold (it moves layers freely
            // while InAar) - hand the vehicle back rather than fight it frame against frame
            if (frames > 0 && SafeInAar())
            {
                HitCamMod.Log("AAR took over after " + frames + " frames, window handed back");
                return true;
            }
            // a heartbeat proves whether the game was still running frames while the window was
            // open, which is the only way to tell our own stall from anybody else's afterwards
            if (elapsed - lastBeat >= 1f)
            {
                HitCamMod.Log("window alive: t=" + elapsed.ToString("0.0") + " frame=" + Time.frameCount
                    + " visuals=" + states.Count);
                lastBeat = elapsed;
            }
            if (elapsed >= HitCamMod.Duration) return true;

            yaw += HitCamMod.OrbitSpeed * deltaTime;
            AimCamera();
            RenderFrame();
            return false;
        }

        static bool SafeInAar()
        {
            try { return AarController.InAar; }
            catch { return false; }
        }

        void RenderFrame()
        {
            // no material swapping in here: the visuals stay lit for the whole window, because
            // replacing sharedMaterials twice per frame for a few hundred renderers is the one
            // thing in this design that can stall the main thread
            try
            {
                float t0 = Time.realtimeSinceStartup;
                replayCamera.Render();
                xrayCamera.Render();
                float ms = (Time.realtimeSinceStartup - t0) * 1000f;
                frames++;
                if (ms > worstRenderMs) worstRenderMs = ms;
                renderFailures = 0;
            }
            catch (Exception e)
            {
                if (++renderFailures == 1 || renderFailures == 5)
                    HitCamMod.Log("render threw (" + renderFailures + "): " + e.Message);
            }

            if (renderFailures < 5) return;
            HitCamMod.Log("render failed " + renderFailures + " times in a row, window aborted");
            elapsed = HitCamMod.Duration;
        }

        // explicit trig instead of Quaternion.Euler: Unity pitches +X *upward*, so an
        // Euler-built -radius offset sat the lens below the hull and into the terrain
        void AimCamera()
        {
            centre = pivot.position;
            Vector3 aim = centre + Vector3.up * aimUp;

            float pitch = Mathf.Clamp(HitCamMod.Elevation, 6f, 70f) * Mathf.Deg2Rad;
            float yawRad = yaw * Mathf.Deg2Rad;
            Vector3 pos = aim + new Vector3(
                Mathf.Sin(yawRad) * radius * Mathf.Cos(pitch),
                radius * Mathf.Sin(pitch),
                Mathf.Cos(yawRad) * radius * Mathf.Cos(pitch));

            // even at the lowest allowed angle the lens stays clear of the ground plane
            pos.y = Mathf.Max(pos.y, centre.y + 2f);

            cameraRoot.transform.position = pos;
            cameraRoot.transform.LookAt(aim);
        }

        public void Dispose()
        {
            try
            {
                HitCamMod.Log("window done: " + frames + " frames, worst render "
                    + worstRenderMs.ToString("0.0") + " ms, " + states.Count + " visuals lit");
                RestoreVisuals();
                RestoreLayers();

                if (uiRoot != null) UnityEngine.Object.Destroy(uiRoot);
                if (cameraRoot != null) UnityEngine.Object.Destroy(cameraRoot);
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }

                if (tracesCreated) RemoveOwnTraces();

                states.Clear();
                suppressedTraces.Clear();
                createdTraces.Clear();
            }
            catch (Exception e)
            {
                HitCamMod.Log("dispose threw: " + e.Message);
            }
        }
    }
}
