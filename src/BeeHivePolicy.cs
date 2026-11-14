namespace LethalFakeClient;

internal static class BeeHivePolicy
{
    internal static bool Guarding(bool active, bool leaving, float horizontalDistanceSquared, float height, float defenseDistance)
    {
        float radius = System.Math.Max(6f, defenseDistance);
        return active && !leaving && height <= 4f && horizontalDistanceSquared <= radius * radius;
    }
}
