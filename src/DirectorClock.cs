namespace LethalFakeClient;

internal static class DirectorClock
{
    internal static bool ShipSupportReady(float normalizedTime, int numberOfHours, float startHour)
    {
        return numberOfHours > 0 && normalizedTime * numberOfHours + 6f >= startHour;
    }
    internal static bool EarlyVoteReturn(bool landed, bool leaving, bool inShipPhase, int votes)
    {
        return landed && !leaving && !inShipPhase && votes > 0;
    }

    internal static float ExtractionHour(int crewCount, int deaths, float returnHour = 22f, bool earlyReturn = true)
    {
        float baseHour = System.Math.Max(6f, System.Math.Min(24f, returnHour)) - 6f;
        if (!earlyReturn || crewCount <= 0 || deaths <= 0)
            return baseHour;
        float fraction = System.Math.Min(crewCount, deaths) / (float)crewCount;
        return System.Math.Max(0f, baseHour - 4f * fraction);
    }

    internal static bool IsExtractionTime(float normalizedTime, int numberOfHours, int crewCount = 0, int deaths = 0,
        float returnHour = 22f, bool earlyReturn = true)
    {
        return numberOfHours > 0 && normalizedTime * numberOfHours >= ExtractionHour(crewCount, deaths, returnHour, earlyReturn);
    }
}
