namespace LethalFakeClient;

internal static class ExtractionPolicy
{
    internal static bool CanCollect(float secondsLeft, float tripDistance, float weight, int carriedValue, int itemValue)
    {
        if (float.IsPositiveInfinity(secondsLeft))
            return true;
        float speed = 2.4f / System.Math.Max(1f, weight);
        float travelTime = System.Math.Max(0f, tripDistance) / speed;
        float reserve = 15f + travelTime * 0.3f;
        if (carriedValue > itemValue && carriedValue > 0)
            reserve += System.Math.Min(15f, 5f * carriedValue / System.Math.Max(1, itemValue));
        return travelTime + reserve + 4f < secondsLeft;
    }
}
