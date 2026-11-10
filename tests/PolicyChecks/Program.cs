using System;
using LethalFakeClient;

static void Check(bool result, string name)
{
    if (!result)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}");
}

Check(NutcrackerPolicy.HasTarget(2, 0, 4), "attacking nutcracker has a valid target");
Check(NutcrackerPolicy.HasTarget(2, 3, 4), "last player slot is valid");
Check(!NutcrackerPolicy.HasTarget(1, 1, 4), "inspection is not a chase");
Check(!NutcrackerPolicy.HasTarget(2, -1, 4), "missing target is ignored");
Check(!NutcrackerPolicy.HasTarget(2, 4, 4), "out-of-range target is ignored");
Check(DirectorClock.ExtractionHour(4, 0) == 16f, "default return is 22:00");
Check(DirectorClock.ExtractionHour(4, 1) == 15f, "death advances return by one hour");
Check(DirectorClock.ExtractionHour(4, 1, 22f, false) == 16f, "early return disabled");
Check(DirectorClock.ExtractionHour(4, 2, 20f) == 12f, "custom return and deaths");
Check(DirectorClock.ExtractionHour(4, 4, 7f) == 0f, "return cannot precede day start");
Check(MovementPolicy.WormThreat(true, false, 10000f, 100f), "targeted worm noticed without sight");
Check(MovementPolicy.WormThreat(false, true, 144f, 18f), "worm eruption boundary");
Check(!MovementPolicy.WormThreat(false, true, 145f, 0f), "outside worm eruption area");
Check(!MovementPolicy.WormThreat(false, true, 0f, 19f), "worm on different elevation");
Check(!MovementPolicy.WormThreat(false, false, 0f, 0f), "roaming worm not erupting");
Check(MovementPolicy.Lane(1) != MovementPolicy.Lane(2), "bot IDs choose different lanes");
Check(SpikeTimingPolicy.CanEnter(false, true, true, 100f, 1f, 1f), "inactive spike trap");
Check(!SpikeTimingPolicy.CanEnter(true, true, false, 0f, 0f, 1f), "closing spike trap");
Check(SpikeTimingPolicy.CanEnter(true, false, true, 2f, 8f, 1f), "open spike trap with time");
Check(!SpikeTimingPolicy.CanEnter(true, false, true, 6.8f, 8f, 1f), "spike trap closing soon");
