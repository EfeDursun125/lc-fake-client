using System;
using HarmonyLib;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch]
internal static class EnemySpecialAttacks
{
    private static readonly Action<CentipedeAI> DamageClingingPlayer =
        AccessTools.MethodDelegate<Action<CentipedeAI>>(AccessTools.Method(typeof(CentipedeAI), "DamagePlayerOnIntervals"));
    private static bool botClingDrop;
    private static readonly AccessTools.FieldRef<MaskedPlayerEnemy, bool> MaskedStartingKill =
        AccessTools.FieldRefAccess<MaskedPlayerEnemy, bool>("startingKillAnimationLocalClient");
    private static readonly AccessTools.FieldRef<MaskedPlayerEnemy, float> MaskedLastEntrance =
        AccessTools.FieldRefAccess<MaskedPlayerEnemy, float>("timeAtLastUsingEntrance");

    [HarmonyPatch(typeof(MaskedPlayerEnemy), nameof(MaskedPlayerEnemy.OnCollideWithPlayer))]
    [HarmonyPostfix]
    private static void MaskedBotCollision(MaskedPlayerEnemy __instance, Collider other)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || __instance.isEnemyDead ||
            __instance.inSpecialAnimation || __instance.stunNormalizedTimer >= 0f || MaskedStartingKill(__instance) ||
            !__instance.ventAnimationFinished || other == null)
            return;
        var player = other.GetComponent<PlayerControllerB>();
        if (player == null || player != EnemyBotContext.Current || !BotBody.TryGet(player, out var body) || body.Dead ||
            !__instance.PlayerIsTargetable(player) || Time.realtimeSinceStartup - MaskedLastEntrance(__instance) < 1.75f)
            return;
        MaskedStartingKill(__instance) = true;
        __instance.KillPlayerAnimationServerRpc((int)player.playerClientId);
    }

    private static void KillContextBot(int playerId)
    {
        var network = NetworkManager.Singleton;
        var round = StartOfRound.Instance;
        if (network == null || !network.IsHost || round == null || (uint)playerId >= (uint)round.allPlayerScripts.Length)
            return;
        var player = round.allPlayerScripts[playerId];
        if (player == EnemyBotContext.Current && BotBody.TryGet(player, out var body))
            body.ApplyDamage(int.MaxValue, CauseOfDeath.Mauling, 0, Vector3.zero, true);
    }

    [HarmonyPatch(typeof(MouthDogAI), nameof(MouthDogAI.KillPlayerServerRpc))]
    [HarmonyPrefix]
    private static void DogAttack(int playerId) => KillContextBot(playerId);

    [HarmonyPatch(typeof(CaveDwellerAI), nameof(CaveDwellerAI.KillPlayerAnimationServerRpc))]
    [HarmonyPrefix]
    private static void CaveDwellerAttack(int playerObjectId) => KillContextBot(playerObjectId);

    [HarmonyPatch(typeof(CentipedeAI), "ClingToPlayer")]
    [HarmonyPrefix]
    private static void BeginCling(PlayerControllerB playerScript)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && BotBody.TryGet(playerScript, out var body))
        {
            body.DropAll();
            botClingDrop = true;
        }
    }

    [HarmonyPatch(typeof(CentipedeAI), "ClingToPlayer")]
    [HarmonyFinalizer]
    private static void EndCling() => botClingDrop = false;

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems))]
    [HarmonyPrefix]
    private static bool PreventHostHudChange(PlayerControllerB __instance)
    {
        return !botClingDrop || !BotBody.Contains(__instance);
    }

    [HarmonyPatch(typeof(CentipedeAI), nameof(CentipedeAI.Update))]
    [HarmonyPostfix]
    private static void CentipedeDamage(CentipedeAI __instance, bool ___clingingToCeiling, ref bool ___triggeredFall)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsHost || __instance.isEnemyDead || StartOfRound.Instance == null)
            return;
        if (__instance.currentBehaviourStateIndex == 3 && __instance.clingingToPlayer != null &&
            BotBody.TryGet(__instance.clingingToPlayer, out var body) && !body.Dead)
        {
            DamageClingingPlayer(__instance);
            return;
        }
        if (__instance.currentBehaviourStateIndex != 1 || !___clingingToCeiling || ___triggeredFall ||
            __instance.clingingToPlayer != null)
            return;
        var ray = new Ray(__instance.transform.position, Vector3.down);
        if (!Physics.SphereCast(ray, 2.15f, out var hit, 20f, StartOfRound.Instance.playersMask) ||
            hit.transform == null || !hit.transform.TryGetComponent<PlayerControllerB>(out var player) ||
            !BotBody.TryGet(player, out var target) || target.Dead ||
            Physics.Linecast(player.transform.position, __instance.transform.position,
                StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
            return;
        ___triggeredFall = true;
        __instance.TriggerCentipedeFallServerRpc(network.LocalClientId);
    }

    [HarmonyPatch(typeof(NutcrackerEnemyAI), nameof(NutcrackerEnemyAI.Update))]
    [HarmonyPostfix]
    private static void NutcrackerBotTarget(NutcrackerEnemyAI __instance, ref bool ___isInspecting,
        ref float ___timeSinceFiringGun, float ___timeSinceHittingPlayer, float ___timeSinceSeeingTarget,
        bool ___aimingGun, bool ___reloadingGun)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || __instance.isEnemyDead ||
            StartOfRound.Instance == null)
            return;
        if (__instance.currentBehaviourStateIndex == 1 && ___isInspecting)
        {
            foreach (var player in BotBody.Players)
            {
                if (player == null || !BotBody.TryGet(player, out var body) || body.Dead ||
                    !__instance.PlayerIsTargetable(player) ||
                    !__instance.CheckLineOfSightForPosition(player.gameplayCamera.transform.position, 70f, 60, 1f) ||
                    !body.MovingForNutcracker(Vector3.Distance(player.transform.position, __instance.transform.position)))
                    continue;
                ___isInspecting = false;
                __instance.SeeMovingThreatServerRpc((int)player.playerClientId);
                return;
            }
        }
        int targetId = __instance.lastPlayerSeenMoving;
        if (__instance.currentBehaviourStateIndex != 2 || (uint)targetId >= (uint)StartOfRound.Instance.allPlayerScripts.Length ||
            __instance.gun == null || __instance.gun.shotgunRayPoint == null || ___aimingGun || ___reloadingGun ||
            ___timeSinceFiringGun <= 0.75f || ___timeSinceHittingPlayer <= 1f || ___timeSinceSeeingTarget >= 8f)
            return;
        var target = StartOfRound.Instance.allPlayerScripts[targetId];
        if (target == null || !BotBody.TryGet(target, out var targetBody) || targetBody.Dead ||
            !__instance.CheckLineOfSightForPosition(target.gameplayCamera.transform.position, 70f, 25, 1f) ||
            Vector3.Angle(__instance.gun.shotgunRayPoint.forward,
                target.gameplayCamera.transform.position - __instance.gun.shotgunRayPoint.position) >= 30f)
            return;
        ___timeSinceFiringGun = 0f;
        if (__instance.agent != null && __instance.agent.enabled && __instance.agent.isOnNavMesh)
            __instance.agent.speed = 0f;
        __instance.AimGunServerRpc(__instance.transform.position);
    }

    [HarmonyPatch(typeof(ShotgunItem), nameof(ShotgunItem.ShootGun))]
    [HarmonyPostfix]
    private static void ShotgunDamage(ShotgunItem __instance, Vector3 shotgunPosition, Vector3 shotgunForward)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || StartOfRound.Instance == null)
            return;
        foreach (var player in BotBody.Players)
        {
            if (player == null || !player.isPlayerControlled || player.isPlayerDead || player == __instance.playerHeldBy)
                continue;
            Vector3 point = player.playerCollider != null ? player.playerCollider.ClosestPoint(shotgunPosition) : player.transform.position + Vector3.up;
            float distance = Vector3.Distance(player.transform.position, shotgunPosition);
            if (distance >= 30f || Vector3.Angle(shotgunForward, point - shotgunPosition) >= 30f ||
                Physics.Linecast(shotgunPosition, point, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                continue;
            int damage = CombatRules.ShotgunDamage(distance);
            player.DamagePlayer(damage, true, true, CauseOfDeath.Gunshots, 0, false, shotgunForward * 30f);
        }
    }
}
