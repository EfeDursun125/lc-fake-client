using BepInEx.Configuration;
using UnityEngine;

namespace LethalFakeClient;

internal static class BotSettings
{
    private static ConfigEntry<int> botLimit = null!;
    private static ConfigEntry<float> viewAngle = null!;
    private static ConfigEntry<float> riskTolerance = null!;
    private static ConfigEntry<bool> collectItems = null!;
    private static ConfigEntry<bool> defend = null!;
    private static ConfigEntry<bool> recoverBodies = null!;
    private static ConfigEntry<bool> shipSupport = null!;
    private static ConfigEntry<float> returnHour = null!;
    private static ConfigEntry<bool> returnEarlierAfterDeaths = null!;

    internal static int BotLimit => Mathf.Clamp(botLimit.Value, 0, 128);
    internal static float ViewAngle => Mathf.Clamp(viewAngle.Value, 60f, 360f);
    internal static float RiskTolerance => Mathf.Clamp(riskTolerance.Value, 0.5f, 1.5f);
    internal static bool CollectItems => collectItems.Value;
    internal static bool Defend => defend.Value;
    internal static bool RecoverBodies => recoverBodies.Value;
    internal static bool ShipSupport => shipSupport.Value;
    internal static float ReturnHour => Mathf.Clamp(returnHour.Value, 6f, 24f);
    internal static bool ReturnEarlierAfterDeaths => returnEarlierAfterDeaths.Value;

    internal static void Bind(ConfigFile config)
    {
        botLimit = config.Bind("Crew", "BotLimit", 128, "Maximum bots, limited by available player slots.");
        viewAngle = config.Bind("Perception", "ViewAngle", 210f, "Horizontal field of view in degrees; very close threats are always noticed.");
        riskTolerance = config.Bind("Behavior", "RiskTolerance", 1f, "Risk tolerance from 0.5 (cautious) to 1.5 (bold).");
        collectItems = config.Bind("Behavior", "CollectItems", true, "Assign scrap collection tasks.");
        recoverBodies = config.Bind("Behavior", "RecoverBodies", true, "Recover fallen crew members when the route is safe.");
        defend = config.Bind("Behavior", "DefendWithWeapons", true, "Swing at nearby threats while continuing the current movement.");
        shipSupport = config.Bind("Behavior", "ShipSupport", true, "Allow a ship guardian to assist the crew with equipment.");
        returnHour = config.Bind("Extraction", "ReturnHour", 22f, new ConfigDescription("Time when bots return to the ship, using the 24-hour game clock.", new AcceptableValueRange<float>(6f, 24f)));
        returnEarlierAfterDeaths = config.Bind("Extraction", "ReturnEarlierAfterDeaths", true, "Return earlier in proportion to crew deaths.");
    }
}
