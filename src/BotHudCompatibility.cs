using System;
using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalFakeClient;

internal static class BotItemActions
{
    internal static void Equip(GrabbableObject item)
    {
        using var scope = BotHudScope.Enter();
        item.EquipItem();
    }

    internal static void Pocket(GrabbableObject item)
    {
        using var scope = BotHudScope.Enter();
        item.PocketItem();
    }

    internal static void Discard(GrabbableObject item)
    {
        using var scope = BotHudScope.Enter();
        item.DiscardItem();
    }
}

[HarmonyPatch]
internal static class BotItemHudPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(HUDManager), nameof(HUDManager.ClearControlTips));
        yield return AccessTools.Method(typeof(HUDManager), nameof(HUDManager.ChangeControlTip));
        yield return AccessTools.Method(typeof(HUDManager), nameof(HUDManager.ChangeControlTipMultiple));
        yield return AccessTools.Method(typeof(HUDManager), nameof(HUDManager.DisplayTip));
    }

    private static bool Prefix() => !BotHudScope.Active;
}

[HarmonyPatch(typeof(PlayerControllerB), "GrabObjectClientRpc")]
internal static class BotGrabHudPatch
{
    private static void Prefix(PlayerControllerB __instance, out BotHudScope.Scope? __state)
    {
        __state = BotBody.Contains(__instance) ? BotHudScope.Enter() : null;
    }

    private static Exception? Finalizer(Exception? __exception, BotHudScope.Scope? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
