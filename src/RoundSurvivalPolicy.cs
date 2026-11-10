namespace LethalFakeClient;

internal static class RoundSurvivalPolicy
{
    internal static bool ContinueAfterHumanDeaths(bool midnight, int livingHumans, bool livingBot)
    {
        return !midnight && livingHumans <= 0 && livingBot;
    }
}
