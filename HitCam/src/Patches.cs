using System.Reflection;
using GHPC.Weapons;
using HarmonyLib;

namespace HitCam
{
    // doDestroy has two early-return paths, so only the _didDestroy false -> true
    // transition means the shot really terminated and ShotInfo is fully populated.
    [HarmonyPatch(typeof(LiveRound), "doDestroy")]
    internal static class LiveRound_DoDestroy
    {
        static readonly FieldInfo DidDestroy = AccessTools.Field(typeof(LiveRound), "_didDestroy");

        static void Prefix(LiveRound __instance, out bool __state)
        {
            __state = DidDestroy == null || (bool)DidDestroy.GetValue(__instance);
        }

        static void Postfix(LiveRound __instance, bool __state)
        {
            if (__state) return;
            if (DidDestroy == null || !(bool)DidDestroy.GetValue(__instance)) return;
            HitCamMod.OnShotTerminated(__instance.ShotInfo);
        }
    }
}
