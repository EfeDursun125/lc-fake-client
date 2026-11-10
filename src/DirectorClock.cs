namespace LethalFakeClient;

internal static class DirectorClock
{
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
