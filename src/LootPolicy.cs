using UnityEngine;

namespace LethalFakeClient;

internal static class LootPolicy
{
    internal static float Score(int value, float weight, float distance, float timePressure, bool twoHanded, int freeSlots,
        float quotaGap = 0f, float quotaUrgency = 0f)
    {
        float weightCost = Mathf.Max(0f, weight - 1f);
        float effort = 12f + distance * (1f + weightCost * 0.75f) + weightCost * 12f;
        float score = Mathf.Max(0, value) / effort;
        if (twoHanded && freeSlots > 1)
            score *= 0.65f;
        score *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(timePressure));
        if (quotaGap > 0f && quotaUrgency > 0f)
        {
            float coverage = Mathf.Clamp01(Mathf.Max(0, value) / quotaGap);
            score += coverage * Mathf.Clamp01(quotaUrgency) * 2f / (1f + distance * 0.04f + weightCost);
        }
        return score;
    }

}
