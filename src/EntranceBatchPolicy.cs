using System.Collections.Generic;

namespace LethalFakeClient;

internal sealed class EntranceBatchPolicy
{
    private const int MinimumBatch = 4;
    private readonly HashSet<int> active = new HashSet<int>();
    private readonly List<int> completed = new List<int>();

    internal bool IsActive(int entranceId) => active.Contains(entranceId);

    internal void Refresh(Dictionary<int, int> counts)
    {
        foreach (var pair in counts)
            if (pair.Value >= MinimumBatch)
                active.Add(pair.Key);
        completed.Clear();
        foreach (int entranceId in active)
            if (!counts.TryGetValue(entranceId, out int remaining) || remaining <= 0)
                completed.Add(entranceId);
        foreach (int entranceId in completed)
            active.Remove(entranceId);
    }
}
