namespace LethalFakeClient;

internal static class MovementPolicy
{
    internal static int FallDamage(float velocity, bool landed)
    {
        return !landed || velocity < -48.5f ? 100 : velocity < -45f ? 80 :
            velocity < -40f ? 50 : velocity < -38f ? 30 : 0;
    }

    internal static bool CanJumpGap(float horizontalDistanceSquared, float height)
    {
        return horizontalDistanceSquared >= 0.36f && horizontalDistanceSquared <= 16f && height >= -1.25f && height <= 1.25f;
    }

    internal static int Lane(ulong playerId) => (int)(playerId % 5UL) - 2;

    internal static bool MovesSideways(float right, float forward)
    {
        return System.Math.Abs(right) > System.Math.Abs(forward);
    }

    internal static float AnimationDirection(float right, float forward, bool sideways)
    {
        return (sideways ? right : forward) < 0f ? -1f : 1f;
    }

    internal static bool AcceptWaypoint(float directLength, float firstLength, float secondLength,
        float startAlignment, float transitionAlignment)
    {
        return directLength > 0f && firstLength > 0f && secondLength > 0f &&
            firstLength + secondLength <= directLength * 1.2f + 1f &&
            startAlignment >= 0.25f && transitionAlignment >= -0.1f;
    }

    internal static float RiskWeightedDistance(float distance, float risk)
    {
        return distance * (1f + 6f * System.Math.Max(0f, risk));
    }

    internal static bool WormThreat(bool targeted, bool emerging, float distanceSquared, float height)
    {
        return targeted || emerging && height <= 18f && distanceSquared <= 144f;
    }
}
