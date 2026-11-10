namespace LethalFakeClient;

internal static class NutcrackerPolicy
{
    internal static bool HasTarget(int state, int targetIndex, int playerCount)
    {
        return state == 2 && targetIndex >= 0 && targetIndex < playerCount;
    }
}
