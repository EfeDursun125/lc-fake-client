namespace LethalFakeClient;

internal static class HoarderPolicy
{
    internal static bool IsHostile(int behaviourState) => behaviourState == 2;

    internal static bool ProtectsLoot(float horizontalDistanceSquared, float heightDifference)
    {
        return heightDifference <= 3f && horizontalDistanceSquared <= 36f;
    }
}
