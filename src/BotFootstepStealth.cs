using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalFakeClient;

[HarmonyPatch]
internal static class BotFootstepStealth
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.PlayFootstepLocal));
        yield return AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.PlayFootstepServer));
    }

    [HarmonyPrefix]
    private static bool Prefix(PlayerControllerB __instance)
    {
        return !__instance.isCrouching || !BotBody.Contains(__instance);
    }
}
