namespace LethalFakeClient;

internal static class ReplicaPolicy
{
    internal static bool ShouldAnchor(bool shipLeaving, bool aboard) => shipLeaving && aboard;

    internal static bool ShouldReleaseAnchor(bool shipLanded, bool shipLeaving) => shipLanded && !shipLeaving;
}
