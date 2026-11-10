using System.Collections.Generic;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class SpikeTrapSafety
{
    private static readonly AccessTools.FieldRef<SpikeRoofTrap, bool> IntervalMode =
        AccessTools.FieldRefAccess<SpikeRoofTrap, bool>("slamOnIntervals");
    private static readonly AccessTools.FieldRef<SpikeRoofTrap, float> Interval =
        AccessTools.FieldRefAccess<SpikeRoofTrap, float>("slamInterval");
    private readonly List<(SpikeRoofTrap Trap, Collider? Area)> traps = new List<(SpikeRoofTrap, Collider?)>();
    private float nextDiscovery;

    internal void Refresh(float now)
    {
        if (now < nextDiscovery)
            return;
        nextDiscovery = now + 5f;
        traps.Clear();
        foreach (var trap in Object.FindObjectsOfType<SpikeRoofTrap>())
        {
            if (trap == null)
                continue;
            Collider? area = trap.GetComponent<Collider>();
            if (area == null)
                foreach (var collider in trap.GetComponentsInChildren<Collider>())
                    if (collider != null && collider.isTrigger)
                    {
                        area = collider;
                        break;
                    }
            traps.Add((trap, area));
        }
    }

    private static Bounds Area(SpikeRoofTrap trap, Collider? collider)
    {
        Bounds bounds = collider != null && collider.enabled ? collider.bounds :
            new Bounds(trap.laserEye != null ? trap.laserEye.position : trap.transform.position, new Vector3(3f, 5f, 3f));
        bounds.Expand(new Vector3(1f, 3f, 1f));
        return bounds;
    }

    internal SpikeRoofTrap? Crossing(Vector3 from, Vector3 to, bool includeStartingArea = false)
    {
        Vector3 segment = to - from;
        float length = segment.magnitude;
        if (length < 0.01f)
            return null;
        foreach (var entry in traps)
        {
            var trap = entry.Trap;
            if (trap == null || !trap.isActiveAndEnabled || !trap.trapActive)
                continue;
            Bounds bounds = Area(trap, entry.Area);
            Vector3 start = from + Vector3.up;
            Vector3 end = to + Vector3.up;
            if (!includeStartingArea && bounds.Contains(start))
                continue;
            if (bounds.Contains(end) || bounds.IntersectRay(new Ray(start, segment / length), out float distance) && distance <= length)
                return trap;
        }
        return null;
    }

    internal bool Contains(SpikeRoofTrap trap, Vector3 point)
    {
        foreach (var entry in traps)
            if (entry.Trap == trap)
                return Area(trap, entry.Area).Contains(point + Vector3.up);
        return false;
    }

    internal bool At(Vector3 point)
    {
        foreach (var entry in traps)
            if (entry.Trap != null && entry.Trap.isActiveAndEnabled && entry.Trap.trapActive &&
                Area(entry.Trap, entry.Area).Contains(point + Vector3.up))
                return true;
        return false;
    }

    internal static bool SafeToEnter(SpikeRoofTrap trap, float crossingSeconds)
    {
        if (trap == null || !trap.trapActive || !trap.isActiveAndEnabled)
            return true;
        return SpikeTimingPolicy.CanEnter(true, trap.slammingDown, IntervalMode(trap),
            Time.realtimeSinceStartup - trap.timeSinceMovingUp, Interval(trap), crossingSeconds);
    }
}

[HarmonyPatch]
internal static class SpikeTrapBotHooks
{
    private static readonly AccessTools.FieldRef<SpikeRoofTrap, bool> IntervalMode =
        AccessTools.FieldRefAccess<SpikeRoofTrap, bool>("slamOnIntervals");

    [HarmonyPatch(typeof(SpikeRoofTrap), "Update")]
    [HarmonyPostfix]
    private static void DetectBot(SpikeRoofTrap __instance)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || !__instance.trapActive ||
            __instance.slammingDown || IntervalMode(__instance) || __instance.laserEye == null || StartOfRound.Instance == null ||
            Time.realtimeSinceStartup - __instance.timeSinceMovingUp < 1.2f)
            return;
        bool nearbyBot = false;
        foreach (var candidate in BotBody.Players)
            if (candidate != null && candidate.isPlayerControlled && !candidate.isPlayerDead &&
                (candidate.transform.position - __instance.laserEye.position).sqrMagnitude < 64f)
            {
                nearbyBot = true;
                break;
            }
        if (!nearbyBot)
            return;
        if (!Physics.Raycast(__instance.laserEye.position, __instance.laserEye.forward, out var hit, 4.4f,
            StartOfRound.Instance.collidersRoomMaskDefaultAndPlayers, QueryTriggerInteraction.Ignore))
            return;
        var player = hit.collider != null ? hit.collider.GetComponent<GameNetcodeStuff.PlayerControllerB>() : null;
        if (player == null || !BotBody.TryGet(player, out var body) || body.Dead)
            return;
        __instance.SpikeTrapSlam();
        if (StartOfRound.Instance.localPlayerController != null)
            __instance.SpikeTrapSlamServerRpc((int)StartOfRound.Instance.localPlayerController.playerClientId);
    }

    [HarmonyPatch(typeof(SpikeRoofTrap), "OnTriggerStay")]
    [HarmonyPrefix]
    private static void DamageBot(SpikeRoofTrap __instance, Collider other)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || !__instance.trapActive ||
            !__instance.slammingDown || Time.realtimeSinceStartup - __instance.timeSinceMovingUp < 0.75f || other == null)
            return;
        var player = other.GetComponent<GameNetcodeStuff.PlayerControllerB>();
        if (player != null && BotBody.TryGet(player, out var body))
            body.ApplyDamage(int.MaxValue, CauseOfDeath.Crushing, 0, Vector3.down * 17f, true);
    }
}
