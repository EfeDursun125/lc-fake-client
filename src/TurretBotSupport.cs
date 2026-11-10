using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch]
internal static class TurretBotSupport
{
    [HarmonyPatch(typeof(Turret), nameof(Turret.CheckForPlayersInLineOfSight))]
    [HarmonyPostfix]
    private static void FindBot(Turret __instance, float radius, bool angleRangeCheck, ref PlayerControllerB __result)
    {
        if (__result != null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost ||
            !__instance.turretActive || __instance.aimPoint == null || __instance.centerPoint == null ||
            __instance.forwardFacingPos == null || StartOfRound.Instance == null)
            return;
        Vector3 origin = __instance.centerPoint.position;
        float best = 900f;
        float angle = __instance.rotationRange / Mathf.Max(0.1f, radius);
        foreach (var player in BotBody.Players)
        {
            if (player == null || !player.isPlayerControlled || player.isPlayerDead || !player.isInsideFactory ||
                player.gameplayCamera == null)
                continue;
            Vector3 target = player.gameplayCamera.transform.position;
            Vector3 direction = target - origin;
            float distance = direction.sqrMagnitude;
            if (distance < 0.01f || distance >= best || Vector3.Angle(__instance.aimPoint.forward, direction) > angle ||
                angleRangeCheck && Vector3.Angle(__instance.forwardFacingPos.forward, direction) > __instance.rotationRange ||
                Physics.Linecast(origin, target, StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
                continue;
            if (Physics.Raycast(origin, direction.normalized, out var hit, Mathf.Sqrt(distance), 1051400, QueryTriggerInteraction.Ignore))
            {
                var blockingPlayer = hit.collider.GetComponentInParent<PlayerControllerB>();
                if (blockingPlayer != player)
                    continue;
            }
            best = distance;
            __result = player;
        }
    }

    [HarmonyPatch(typeof(Turret), "Update")]
    [HarmonyPrefix]
    private static void DamageBot(Turret __instance, float ___turretInterval, bool ___enteringBerserkMode)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || !__instance.turretActive ||
            ___turretInterval < 0.21f || __instance.aimPoint == null ||
            __instance.turretMode != TurretMode.Firing &&
            (__instance.turretMode != TurretMode.Berserk || ___enteringBerserkMode))
            return;
        var player = __instance.CheckForPlayersInLineOfSight(3f);
        if (player != null && BotBody.TryGet(player, out var body))
            body.ApplyDamage(50, CauseOfDeath.Gunshots, 0, __instance.aimPoint.forward * 40f, true);
    }
}
