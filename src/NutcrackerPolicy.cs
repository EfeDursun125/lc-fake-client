namespace LethalFakeClient;

internal static class NutcrackerPolicy
{
    internal static bool Moving(bool emote, float secondsSinceMove, float secondsSinceTurn, float distance)
    {
        return emote || secondsSinceMove < 0.1f || distance < 10f && secondsSinceTurn < 0.1f;
    }

    internal static bool HasTarget(int state, int targetIndex, int playerCount)
    {
        return state == 2 && targetIndex >= 0 && targetIndex < playerCount;
    }
}
