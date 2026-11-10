using System.Collections.Generic;

namespace LethalFakeClient;

internal sealed class EntranceSafetyMemory
{
    private readonly Dictionary<int, float> unsafeUntil = new Dictionary<int, float>();

    internal void MarkUnsafe(int entranceId, float now)
    {
        unsafeUntil[entranceId] = now + 45f;
    }

    internal bool IsAllowed(int entranceId, float now)
    {
        if (!unsafeUntil.TryGetValue(entranceId, out float until))
            return true;
        if (now < until)
            return false;
        unsafeUntil.Remove(entranceId);
        return true;
    }
}
