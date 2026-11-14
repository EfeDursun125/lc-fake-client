namespace LethalFakeClient;

internal static class HoarderPolicy
{
    internal static bool IsHostile(int behaviourState, bool chasing) => behaviourState == 2 || chasing;

    internal static bool ProtectsLoot(float horizontalDistanceSquared, float heightDifference)
    {
        return heightDifference <= 3f && horizontalDistanceSquared <= 36f;
    }
}
