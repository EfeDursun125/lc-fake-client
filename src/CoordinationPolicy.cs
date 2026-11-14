namespace LethalFakeClient;

internal static class CoordinationPolicy
{
    internal const float ThreatMemorySeconds = 12f;

    internal static bool RelevantThreat(bool outside, bool ready, bool neutralOrHazard)
    {
        return !outside && ready && !neutralOrHazard;
    }

    internal static bool ShouldCoordinate(float now, float lastThreatSeen)
    {
        return now - lastThreatSeen < ThreatMemorySeconds;
    }
}
