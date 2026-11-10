namespace LethalFakeClient;

internal static class CoordinationPolicy
{
    internal const float ThreatMemorySeconds = 30f;

    internal static bool ShouldCoordinate(float now, float lastThreatSeen)
    {
        return now - lastThreatSeen < ThreatMemorySeconds;
    }
}
