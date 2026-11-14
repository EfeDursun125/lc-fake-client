using System.Collections.Generic;

namespace LethalFakeClient;

internal sealed class NetworkGeneration
{
    private readonly Dictionary<int, (ulong Generation, bool Retired)> slots = new Dictionary<int, (ulong, bool)>();
    private ulong session;
    private uint round;
    private bool closed;

    internal void SetSession(ulong value)
    {
        if (session == value)
            return;
        session = value;
        round = 0;
        closed = false;
        slots.Clear();
    }

    internal bool AcceptRound(ulong value, uint generation, out bool changed)
    {
        changed = false;
        if (session == 0 || value != session || generation == 0 || generation < round || closed && generation == round)
            return false;
        if (generation > round)
        {
            round = generation;
            closed = false;
            slots.Clear();
            changed = true;
        }
        return true;
    }

    internal void CloseRound() => closed = true;

    internal bool AcceptSlot(int slot, ulong generation, bool initialize, out bool changed)
    {
        changed = false;
        if (generation == 0)
            return false;
        if (slots.TryGetValue(slot, out var current))
        {
            if (generation == current.Generation)
                return !initialize || !current.Retired;
            if (!initialize || generation < current.Generation)
                return false;
        }
        else if (!initialize)
            return false;
        slots[slot] = (generation, false);
        changed = true;
        return true;
    }

    internal void Retire(int slot, ulong generation)
    {
        if (slots.TryGetValue(slot, out var current) && current.Generation == generation)
            slots[slot] = (generation, true);
    }
}
