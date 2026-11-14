namespace LethalFakeClient;

internal static class RoundStartPolicy
{
    internal static bool CanAcceptNewRound(bool landed, bool inShipPhase, bool leaving)
    {
        return landed && !inShipPhase && !leaving;
    }

    internal static bool NeedsPreparation(bool prepared, bool landed, bool inShipPhase, bool leaving, bool company)
    {
        return !prepared && !company && CanAcceptNewRound(landed, inShipPhase, leaving);
    }
}
