namespace LethalFakeClient;

internal static class MeleePolicy
{
    internal static bool ShouldSwingMasked(float distanceSquared, float predictedDistanceSquared)
    {
        return distanceSquared <= 12.25f && (distanceSquared <= 3.24f || predictedDistanceSquared <= 5.29f);
    }
}
