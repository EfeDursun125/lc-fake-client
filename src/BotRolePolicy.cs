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
        shipSupport = botCount >= 3 ? System.Math.Max(1, botCount / 5) : 0;
        haulers = botCount >= 2 ? System.Math.Max(1, botCount / 3) : 0;
        explorers = System.Math.Max(0, botCount - shipSupport - haulers);
    }

    internal static float RetryDelay(int attempts)
    {
        return System.Math.Min(30f, 4f * (1 << System.Math.Min(3, System.Math.Max(0, attempts - 1))));
    }
}
