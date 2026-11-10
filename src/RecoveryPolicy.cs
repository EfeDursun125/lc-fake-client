namespace LethalFakeClient;

internal static class RecoveryPolicy
{
    internal static bool NoExitTimedOut(float now, float since)
    {
        return since >= 0f && now - since >= 8f;
    }

    internal static bool NoisyLootReady(float now, float lastDogSeen, bool dogNearby)
    {
        return !dogNearby && now - lastDogSeen >= 8f;
    }
}
