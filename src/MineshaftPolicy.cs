namespace LethalFakeClient;

internal static class MineshaftPolicy
{
    internal static bool WaitForCrew(bool owner, bool safe, bool approaching, float elapsed)
    {
        return owner && safe && approaching && elapsed >= 0f && elapsed < 8f;
    }

    internal static bool DifferentFloors(float from, float to, float top, float bottom)
    {
        if (System.Math.Abs(top - bottom) < 6f)
            return false;
        float middle = (top + bottom) * 0.5f;
        return (from - middle) * (to - middle) < 0f;
    }
}
