using HarmonyLib;

namespace LethalFakeClient;

// The server's weather controller selects one item before issuing the strike warning.
internal static class LightningHazard
{
    private static StormyWeather? source;
    private static GrabbableObject? target;

    internal static GrabbableObject? Target => source != null && source.isActiveAndEnabled ? target : null;

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
    }

    internal static bool IsTarget(GrabbableObject? item)
    {
        return item != null && source != null && source.isActiveAndEnabled && target == item &&
            item.itemProperties != null && item.itemProperties.isConductiveMetal && !item.isInFactory;
    }
}

[HarmonyPatch]
internal static class LightningHooks
{
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
