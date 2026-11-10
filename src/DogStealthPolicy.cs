namespace LethalFakeClient;

internal static class DogStealthPolicy
{
    internal static bool WithinAlertRange(float distanceSquared, bool alreadyAlert)
    {
        float radius = alreadyAlert ? 42f : 35f;
        return distanceSquared <= radius * radius;
    }

    internal static bool IsPursuing(bool targetsBot, int state, bool hasDestination, float destinationDistanceSquared)
    {
        return state >= 2 && (targetsBot || hasDestination && destinationDistanceSquared < 36f);
    }
}
