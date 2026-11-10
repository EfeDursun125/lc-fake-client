using System;

namespace LethalFakeClient;

internal static class TeamDecisionPolicy
{
    internal static float RescueScore(int health, bool grabbed, bool trapped, bool turret, float threatDistance, bool pursued)
    {
        float score = grabbed ? 100f : 0f;
        if (trapped)
            score = Math.Max(score, 40f);
        if (turret)
            score = Math.Max(score, 75f);
        if (threatDistance < 8f)
            score = Math.Max(score, 35f + (8f - Math.Max(0f, threatDistance)) * 7f + (pursued ? 12f : 0f));
        return score + Math.Max(0, 100 - health) * 0.25f;
    }

    internal static bool ShouldDropForEscape(bool twoHanded, float weight, float dangerDistance, bool pursued)
    {
        return (twoHanded || weight >= 1.25f) && (dangerDistance < 7f || pursued && dangerDistance < 12f);
    }

    internal static bool ShouldCloseDoor(bool threat, bool crewNearDoor, bool overheated, float power)
    {
        return threat && !crewNearDoor && !overheated && power >= 0.3f;
    }

    internal static bool ShouldOpenDoor(bool threat, float timeSinceThreat, float power)
    {
        return !threat && timeSinceThreat >= 6f || power <= 0.15f;
    }
}
