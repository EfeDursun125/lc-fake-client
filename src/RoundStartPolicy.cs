namespace LethalFakeClient;

internal static class RoundStartPolicy
{
    internal static bool NeedsPreparation(bool prepared, bool landed, bool inShipPhase, bool leaving, bool company)
    {
        return !prepared && landed && !inShipPhase && !leaving && !company;
    }
}
