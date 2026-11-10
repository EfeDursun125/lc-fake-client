using UnityEngine;

namespace LethalFakeClient;

internal static class StaminaPolicy
{
    internal static float Update(float stamina, float elapsed, float sprintTime, float weight, bool sprinting, bool walking, bool hindered)
    {
        float duration = Mathf.Max(0.1f, sprintTime);
        if (sprinting)
            stamina -= elapsed / duration * Mathf.Max(1f, weight);
        else if (hindered && walking)
            stamina -= elapsed / duration * 0.5f;
        else if (!hindered)
            stamina += elapsed / (duration + (walking ? 9f : 4f));
        return Mathf.Clamp01(stamina);
    }

    internal static bool CanSprint(float stamina, bool alreadySprinting, bool injured, bool hindered, bool outside = false)
    {
        if (injured || hindered)
            return false;
        if (alreadySprinting)
            return stamina > 0.1f;
        return outside ? stamina >= 1f : stamina > 0.3f;
    }
}
