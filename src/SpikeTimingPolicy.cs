namespace LethalFakeClient;

internal static class SpikeTimingPolicy
{
    internal static bool CanEnter(bool active, bool slamming, bool timed, float elapsedSinceOpen,
        float interval, float crossingSeconds)
    {
        if (!active)
            return true;
        if (slamming)
            return false;
        return !timed || interval - elapsedSinceOpen > crossingSeconds + 0.35f;
    }
}
