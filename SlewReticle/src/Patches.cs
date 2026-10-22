using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SlewReticle
{
    // The player's own Update is the one place the game aims the turret from the exterior camera, and it
    // runs every frame, so the circle can only win by writing immediately after it. The turret itself
    // slews later, in AimablePlatform::LateUpdate, which reads whatever the fire control was told last.
    [HarmonyPatch(typeof(GHPC.Player.PlayerInput), "Update")]
    internal static class PlayerInput_Update
    {
        static void Postfix(GHPC.Player.PlayerInput __instance)
        {
            SlewReticleMod.ApplyAimFromPatch(__instance);
        }
    }

    // BufferedCameraFollow::LateUpdate is the last thing the game does with the exterior camera in a
    // frame - it aims it, applies its zoom smoothing, positions it behind the target and keeps it out of
    // geometry - so both changes here are made after all of that and nothing rewrites them afterwards.
    [HarmonyPatch(typeof(GHPC.Camera.BufferedCameraFollow), "LateUpdate")]
    internal static class BufferedCameraFollow_LateUpdate
    {
        static readonly FieldInfo CameraField =
            AccessTools.Field(typeof(GHPC.Camera.BufferedCameraFollow), "BufferedCamera");

        static void Postfix(GHPC.Camera.BufferedCameraFollow __instance)
        {
            if (CameraField == null) return;
            Camera cam;
            try { cam = CameraField.GetValue(__instance) as Camera; }
            catch { return; }
            SlewReticleMod.AnchorCamera(__instance, cam);
            SlewReticleMod.ApplyExtraZoom(cam);
        }
    }
}
