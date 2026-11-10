using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch(typeof(SandSpiderWebTrap))]
internal static class SpiderWebHooks
{
    [HarmonyPatch("OnTriggerStay")]
    [HarmonyPrefix]
    private static bool OnTriggerStay(Collider other)
    {
        if (other == null)
            return true;
        var player = other.GetComponentInParent<PlayerControllerB>();
        return player == null || !BotBody.Contains(player);
    }
}
