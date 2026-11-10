namespace LethalFakeClient;

internal static class MovementPolicy
{
    internal static int Lane(ulong playerId) => (int)(playerId % 5UL) - 2;

    internal static bool WormThreat(bool targeted, bool emerging, float distanceSquared, float height)
    {
        return targeted || emerging && height <= 18f && distanceSquared <= 144f;
    }
}
