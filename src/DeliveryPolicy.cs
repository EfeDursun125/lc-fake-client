namespace LethalFakeClient;

internal static class DeliveryPolicy
{
    internal static bool ShouldOpen(bool enabled, bool landed, bool opened, int orderCount)
    {
        return enabled && landed && !opened && orderCount > 0;
    }

    internal static bool PreferDistance(float candidateSquared, float bestSquared)
    {
        return candidateSquared >= 0f && candidateSquared < bestSquared;
    }
}
