namespace LethalFakeClient;

internal static class ExplorationPolicy
{
    internal static bool MayExplore(bool coordinated, bool hasPartner, bool inside, bool nearbyThreat, float localRisk)
    {
        return !inside || !coordinated || hasPartner || !nearbyThreat && localRisk < 0.18f;
    }
}
