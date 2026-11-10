namespace LethalFakeClient;

internal static class AreaHazardPolicy
{
    internal static float Proximity(float horizontalDistance, float verticalDistance, float radius)
    {
        if (verticalDistance > 3f)
            return 0f;
        return System.Math.Max(0f, System.Math.Min(1f, (radius + 1f - horizontalDistance) / 2f));
    }

    internal static bool CanTraverse(float risk, float previousRisk, bool leaving)
    {
        return risk < 0.5f || leaving && risk <= previousRisk + 0.04f;
    }

    internal static bool CanEscapeThroughCadaver(float cadaverRisk, float otherAreaRisk)
    {
        return cadaverRisk >= 0.5f && otherAreaRisk < 0.5f;
    }
}
