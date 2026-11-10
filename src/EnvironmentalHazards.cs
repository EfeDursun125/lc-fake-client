using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace LethalFakeClient;

internal sealed class EnvironmentalHazards : IDisposable
{
    private static readonly AccessTools.FieldRef<Landmine, bool> MineActive =
        AccessTools.FieldRefAccess<Landmine, bool>("mineActivated");
    private static readonly AccessTools.FieldRef<BridgeTrigger, bool> BridgeFallen =
        AccessTools.FieldRefAccess<BridgeTrigger, bool>("hasBridgeFallen");
    private static readonly AccessTools.FieldRef<BridgeTriggerType2, bool> BridgeType2Fallen =
        AccessTools.FieldRefAccess<BridgeTriggerType2, bool>("bridgeFell");
    private static readonly AccessTools.FieldRef<TerminalAccessibleObject, bool> TerminalInCooldown =
        AccessTools.FieldRefAccess<TerminalAccessibleObject, bool>("inCooldown");
    private readonly List<Component> candidates = new List<Component>();
    private readonly Dictionary<Component, NavMeshObstacle> known = new Dictionary<Component, NavMeshObstacle>();
    private readonly List<Component> expired = new List<Component>();
    private readonly Queue<string> warnings = new Queue<string>();
    private readonly int sightMask;
    private float nextDiscovery;
    private float nextWarning;

    internal EnvironmentalHazards(int sightMask)
    {
        this.sightMask = sightMask;
    }

    internal void Refresh(List<IBot> bots, float now)
    {
        if (now >= nextDiscovery)
        {
            nextDiscovery = now + 10f;
            candidates.Clear();
            candidates.AddRange(UnityEngine.Object.FindObjectsOfType<Landmine>());
            candidates.AddRange(UnityEngine.Object.FindObjectsOfType<Turret>());
            candidates.AddRange(UnityEngine.Object.FindObjectsOfType<QuicksandTrigger>());
            candidates.AddRange(UnityEngine.Object.FindObjectsOfType<BridgeTrigger>());
            candidates.AddRange(UnityEngine.Object.FindObjectsOfType<BridgeTriggerType2>());
        }

        foreach (var source in candidates)
        {
            if (source == null || known.ContainsKey(source) || !Active(source))
                continue;
            bool outdoor = source is QuicksandTrigger || source is BridgeTrigger || source is BridgeTriggerType2;
            foreach (var bot in bots)
            {
                if (!bot.Available || bot.Aboard || outdoor == bot.Inside)
                    continue;
                Vector3 eye = bot.Position + Vector3.up * 1.4f;
                Vector3 target = source.transform.position + Vector3.up * 0.3f;
                bool brokenBridge = source is BridgeTrigger || source is BridgeTriggerType2;
                if (!brokenBridge && ((target - eye).sqrMagnitude > 400f ||
                    Physics.Linecast(eye, target, sightMask, QueryTriggerInteraction.Ignore)))
                    continue;
                var marker = new GameObject("LethalFakeClient.HazardObstacle");
                marker.transform.position = source.transform.position;
                var obstacle = marker.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                if ((source is QuicksandTrigger || brokenBridge) && source.TryGetComponent<Collider>(out var boundsCollider))
                {
                    Bounds bounds = boundsCollider.bounds;
                    marker.transform.position = bounds.center;
                    obstacle.size = new Vector3(bounds.size.x + 1f, Mathf.Max(2f, bounds.size.y), bounds.size.z + 1f);
                }
                else
                    obstacle.size = source is Landmine ? new Vector3(3f, 3f, 3f) : new Vector3(4f, 4f, 4f);
                obstacle.carving = true;
                known.Add(source, obstacle);
                if ((source is Landmine || source is Turret) && warnings.Count < 32)
                {
                    var terminal = FindTerminal(source);
                    string code = terminal != null && !string.IsNullOrEmpty(terminal.objectCode) ? $" (terminal: {terminal.objectCode})" : "";
                    string kind = source is Landmine ? "Landmine" : "Turret";
                    warnings.Enqueue($"{bot.Body.playerUsername}: {kind} detected{code}.");
                }
                break;
            }
        }

        expired.Clear();
        foreach (var pair in known)
        {
            if (pair.Key == null || pair.Value == null)
                expired.Add(pair.Key!);
            else
                pair.Value.enabled = Active(pair.Key);
        }
        foreach (var source in expired)
        {
            if (known.TryGetValue(source, out var obstacle) && obstacle != null)
                UnityEngine.Object.Destroy(obstacle.gameObject);
            known.Remove(source);
        }
        if (warnings.Count > 0 && now >= nextWarning && HUDManager.Instance != null)
        {
            nextWarning = now + 4f;
            HUDManager.Instance.AddTextToChatOnServer(warnings.Dequeue());
        }
    }

    private static TerminalAccessibleObject? FindTerminal(Component source)
    {
        var terminal = source.GetComponent<TerminalAccessibleObject>();
        if (terminal == null)
            terminal = source.GetComponentInParent<TerminalAccessibleObject>();
        if (terminal == null)
            terminal = source.GetComponentInChildren<TerminalAccessibleObject>();
        return terminal != null && !terminal.isBigDoor ? terminal : null;
    }

    private static bool Active(Component source)
    {
        if (source == null || !source.gameObject.activeInHierarchy)
            return false;
        if (source is Landmine mine)
            return !mine.hasExploded && MineActive(mine);
        if (source is Turret turret)
            return turret.turretActive;
        if (source is BridgeTrigger bridge)
            return BridgeFallen(bridge);
        if (source is BridgeTriggerType2 bridge2)
            return BridgeType2Fallen(bridge2);
        return source is QuicksandTrigger sand && !sand.isWater && sand.enabled;
    }

    internal float Risk(Vector3 point, bool inside, Turret? ignored = null)
    {
        foreach (var pair in known)
        {
            var source = pair.Key;
            bool outdoor = source is QuicksandTrigger || source is BridgeTrigger || source is BridgeTriggerType2;
            if (source == null || source == ignored || !Active(source) || outdoor == inside)
                continue;
            if ((source is QuicksandTrigger || source is BridgeTrigger || source is BridgeTriggerType2) &&
                source.TryGetComponent<Collider>(out var area))
            {
                Bounds bounds = area.bounds;
                bounds.Expand(new Vector3(1f, 2f, 1f));
                if (bounds.Contains(point))
                    return 1f;
                continue;
            }
            Vector3 delta = point - source.transform.position;
            if (Mathf.Abs(delta.y) > 3f)
                continue;
            if (source is Landmine && delta.sqrMagnitude < 6.25f)
                return 1f;
            if (source is Turret turret && turret.forwardFacingPos != null)
            {
                Vector3 direction = point + Vector3.up - turret.forwardFacingPos.position;
                if (direction.sqrMagnitude < 225f && Vector3.Angle(turret.forwardFacingPos.forward, direction) < 40f &&
                    !Physics.Linecast(turret.forwardFacingPos.position, point + Vector3.up, sightMask, QueryTriggerInteraction.Ignore))
                    return 1f;
            }
        }
        return 0f;
    }

    internal Turret? DangerousTurret(Vector3 position, bool inside, PlayerControllerB bot)
    {
        if (!inside)
            return null;
        foreach (var source in known.Keys)
        {
            if (!(source is Turret turret) || !Active(turret) || turret.aimPoint == null ||
                turret.turretMode != TurretMode.Firing && turret.turretMode != TurretMode.Charging && turret.turretMode != TurretMode.Berserk)
                continue;
            Vector3 target = position + Vector3.up * 1.3f;
            Vector3 direction = target - turret.aimPoint.position;
            if (direction.sqrMagnitude >= 900f || turret.targetPlayerWithRotation != bot &&
                Vector3.Angle(turret.aimPoint.forward, direction) > 30f ||
                Physics.Linecast(turret.aimPoint.position, target, sightMask, QueryTriggerInteraction.Ignore))
                continue;
            return turret;
        }
        return null;
    }

    internal bool Support(List<IBot> bots)
    {
        foreach (var pair in known)
        {
            var source = pair.Key;
            if (source == null || !Active(source) || !(source is Turret || source is Landmine))
                continue;
            var terminal = FindTerminal(source);
            if (terminal == null || string.IsNullOrEmpty(terminal.objectCode) || TerminalInCooldown(terminal))
                continue;
            var round = StartOfRound.Instance;
            if (round == null)
                continue;
            foreach (var player in round.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled || player.isPlayerDead || !player.isInsideFactory ||
                    (player.transform.position - source.transform.position).sqrMagnitude > 225f)
                    continue;
                terminal.CallFunctionFromTerminal();
                Plugin.Log.LogInfo($"Ship operator disabled hazard {source.name} with terminal code {terminal.objectCode}.");
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        foreach (var obstacle in known.Values)
            if (obstacle != null)
                UnityEngine.Object.Destroy(obstacle.gameObject);
        known.Clear();
    }
}
