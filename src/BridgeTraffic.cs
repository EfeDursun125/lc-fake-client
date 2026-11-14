using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace LethalFakeClient;

internal sealed class BridgeTraffic
{
    private static readonly AccessTools.FieldRef<BridgeTrigger, bool> BridgeFallen =
        AccessTools.FieldRefAccess<BridgeTrigger, bool>("hasBridgeFallen");
    private static readonly AccessTools.FieldRef<BridgeTriggerType2, bool> BridgeType2Fallen =
        AccessTools.FieldRefAccess<BridgeTriggerType2, bool>("bridgeFell");

    private readonly List<(Component Source, Collider Area)> bridges = new List<(Component, Collider)>();
    private readonly Dictionary<Component, CrewBot> owners = new Dictionary<Component, CrewBot>();
    private readonly Dictionary<Component, (Vector3 Point, float Time)> progress = new Dictionary<Component, (Vector3, float)>();
    private readonly Dictionary<CrewBot, (Component Source, Bounds Area)> occupied = new Dictionary<CrewBot, (Component, Bounds)>();
    private float nextDiscovery;

    private void Refresh(float now)
    {
        if (now < nextDiscovery)
            return;
        nextDiscovery = now + 5f;
        bridges.Clear();
        foreach (var bridge in Object.FindObjectsOfType<BridgeTrigger>())
            Add(bridge);
        foreach (var bridge in Object.FindObjectsOfType<BridgeTriggerType2>())
            Add(bridge);
    }

    private void Add(Component bridge)
    {
        if (bridge != null && bridge.TryGetComponent<Collider>(out var collider) && collider != null)
            bridges.Add((bridge, collider));
    }

    private static bool Fallen(Component source)
    {
        return source is BridgeTrigger bridge && BridgeFallen(bridge) ||
            source is BridgeTriggerType2 bridge2 && BridgeType2Fallen(bridge2);
    }

    internal void Release(CrewBot bot)
    {
        occupied.Remove(bot);
        foreach (var bridge in bridges)
            if (bridge.Source != null && owners.TryGetValue(bridge.Source, out var owner) && owner == bot)
            {
                owners.Remove(bridge.Source);
                progress.Remove(bridge.Source);
            }
    }

    internal bool Update(CrewBot bot, NavMeshAgent agent, float now, out bool collapsed)
    {
        collapsed = false;
        Refresh(now);
        Vector3 position = bot.Position + Vector3.up * 0.5f;
        bool hadPrevious = occupied.TryGetValue(bot, out var previous);
        if (hadPrevious && previous.Source != null && Fallen(previous.Source) && previous.Area.Contains(position))
        {
            collapsed = true;
            Release(bot);
            return false;
        }
        Component? current = null;
        Bounds currentBounds = default;
        foreach (var entry in bridges)
        {
            if (entry.Source == null || entry.Area == null || !entry.Source.gameObject.activeInHierarchy)
                continue;
            Bounds bounds = entry.Area.bounds;
            if (bounds.Contains(position))
            {
                current = entry.Source;
                currentBounds = bounds;
                break;
            }
        }
        if (current != null)
            occupied[bot] = (current, currentBounds);
        else
            occupied.Remove(bot);
        if (collapsed)
        {
            Release(bot);
            return false;
        }
        if (!agent.isOnNavMesh)
            return false;

        foreach (var entry in bridges)
        {
            var source = entry.Source;
            if (source == null || entry.Area == null || !source.gameObject.activeInHierarchy || Fallen(source))
                continue;
            Bounds bounds = entry.Area.bounds;
            Bounds approachBounds = bounds;
            approachBounds.Expand(new Vector3(6f, 1f, 6f));
            if (owners.TryGetValue(source, out var owner))
            {
                Vector3 ownerPosition = owner.Position;
                if (!progress.TryGetValue(source, out var last) || (ownerPosition - last.Point).sqrMagnitude > 0.09f)
                    progress[source] = (ownerPosition, now);
                else if (!bounds.Contains(ownerPosition + Vector3.up * 0.5f) && now - last.Time > 4f)
                {
                    owners.Remove(source);
                    progress.Remove(source);
                }
                if (!owner.Available || owner.Inside || !approachBounds.Contains(ownerPosition + Vector3.up * 0.5f))
                {
                    owners.Remove(source);
                    progress.Remove(source);
                }
            }
            bool onBridge = source == current;
            if (!onBridge && (!agent.hasPath || agent.isOnOffMeshLink))
                continue;
            Vector3 direction = onBridge ? Vector3.zero : agent.steeringTarget - bot.Position;
            direction.y = 0f;
            bool approaching = false;
            if (!onBridge && direction.sqrMagnitude > 0.01f)
            {
                Bounds protectedBounds = bounds;
                protectedBounds.Expand(new Vector3(1.2f, 0.5f, 1.2f));
                approaching = protectedBounds.Contains(position) ||
                    protectedBounds.IntersectRay(new Ray(position, direction.normalized), out float distance) && distance <= 2.5f;
            }
            if (!onBridge && !approaching)
                continue;
            if (owners.TryGetValue(source, out owner) && owner != bot)
                return !onBridge;
            owners[source] = bot;
            if (!progress.ContainsKey(source))
                progress[source] = (bot.Position, now);
            if (onBridge && (!hadPrevious || previous.Source != source) && source is BridgeTriggerType2 fragile)
                fragile.AddToBridgeInstabilityServerRpc();
            return false;
        }
        return false;
    }
}
