namespace LethalFakeClient;

internal enum BotRole
{
    Explorer,
    Hauler,
    ShipSupport
}

internal static class BotRolePolicy
{
    internal static void Targets(int botCount, out int explorers, out int haulers, out int shipSupport)
    {
        shipSupport = botCount >= 2 ? 1 : 0;
        haulers = botCount >= 3 ? System.Math.Max(1, botCount / 3) : 0;
        explorers = System.Math.Max(0, botCount - shipSupport - haulers);
    }

    internal static bool PreferSupport(int health, bool current, bool aboard, int bestHealth, bool bestCurrent, bool bestAboard)
    {
        if (health != bestHealth)
            return health < bestHealth;
        if (current != bestCurrent)
            return current;
        return aboard && !bestAboard;
    }

    internal static float RetryDelay(int attempts)
    {
        return System.Math.Min(30f, 4f * (1 << System.Math.Min(3, System.Math.Max(0, attempts - 1))));
    }
}
