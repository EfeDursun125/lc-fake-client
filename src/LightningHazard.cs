using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

// The server's weather controller selects one item before issuing the strike warning.
internal static class LightningHazard
{
    private static StormyWeather? source;
    private static GrabbableObject? target;
    private static GrabbableObject? warnedTarget;
    private static float warningUntil;

    internal static GrabbableObject? Target => source != null && source.isActiveAndEnabled ?
        target != null ? target : Time.time < warningUntil ? warnedTarget : null : null;

    internal static void Update(StormyWeather weather, GrabbableObject? item)
    {
        source = weather;
        target = item;
    }

    internal static void Clear(StormyWeather weather)
    {
        if (source != weather)
            return;
        source = null;
        target = null;
        warnedTarget = null;
        warningUntil = 0f;
    }

    internal static void Warn(StormyWeather weather, NetworkObject networkObject, float elapsed)
    {
        if (networkObject == null || !networkObject.TryGetComponent<GrabbableObject>(out var item))
            return;
        source = weather;
        warnedTarget = item;
        warningUntil = Time.time + Mathf.Clamp(10f - elapsed, 0.5f, 10f) + 1f;
    }

    internal static bool IsTarget(GrabbableObject? item)
    {
        return item != null && source != null && source.isActiveAndEnabled &&
            (target == item || Time.time < warningUntil && warnedTarget == item);
    }
}

[HarmonyPatch]
internal static class LightningHooks
{
    [HarmonyPatch(typeof(StormyWeather), nameof(StormyWeather.SetStaticElectricityWarning))]
    [HarmonyPostfix]
    private static void StaticWarning(StormyWeather __instance, NetworkObject warningObject, float particleTime)
    {
        LightningHazard.Warn(__instance, warningObject, particleTime);
    }

    [HarmonyPatch(typeof(StormyWeather), "Update")]
    [HarmonyPostfix]
    private static void WeatherUpdated(StormyWeather __instance, GrabbableObject ___targetingMetalObject)
    {
        LightningHazard.Update(__instance, ___targetingMetalObject);
    }

    [HarmonyPatch(typeof(StormyWeather), "OnDisable")]
    [HarmonyPostfix]
    private static void WeatherDisabled(StormyWeather __instance)
    {
        LightningHazard.Clear(__instance);
    }
}
