using System.Collections.Generic;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch]
internal static class ThumperBotCollision
{
    private sealed class CollisionCache
    {
        internal Collider? Trigger;
        internal float NextCheck;
    }

    private static readonly Dictionary<CrawlerAI, CollisionCache> caches = new Dictionary<CrawlerAI, CollisionCache>();

    [HarmonyPatch(typeof(CrawlerAI), nameof(CrawlerAI.Update))]
    [HarmonyPostfix]
    private static void CheckNearTarget(CrawlerAI __instance)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || StartOfRound.Instance == null || __instance.isEnemyDead ||
            __instance.currentBehaviourStateIndex != 1 || __instance.stunNormalizedTimer >= 0f ||
            __instance.targetPlayer == null || !BotBody.TryGet(__instance.targetPlayer, out var body) || body.Dead)
            return;
        if (!caches.TryGetValue(__instance, out var cache))
        {
            cache = new CollisionCache();
            var detector = __instance.GetComponentInChildren<EnemyAICollisionDetect>();
            if (detector != null)
                cache.Trigger = detector.GetComponent<Collider>();
            caches.Add(__instance, cache);
        }
        if (Time.time < cache.NextCheck)
            return;
        cache.NextCheck = Time.time + 0.15f;
        var player = __instance.targetPlayer;
        var collider = player.thisController;
        var trigger = cache.Trigger;
        if (collider == null || !collider.enabled || trigger == null || !trigger.enabled || !trigger.gameObject.activeInHierarchy ||
            collider.GetComponent<GameNetcodeStuff.PlayerControllerB>() != player)
            return;
        Vector3 enemyPoint = trigger.ClosestPoint(collider.bounds.center);
        Vector3 playerPoint = collider.ClosestPoint(enemyPoint);
        if ((playerPoint - enemyPoint).sqrMagnitude > 0.36f ||
            Physics.Linecast(trigger.bounds.center, collider.bounds.center,
                StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            return;
        __instance.OnCollideWithPlayer(collider);
    }

    internal static void Forget(EnemyAI enemy)
    {
        if (enemy is CrawlerAI crawler)
            caches.Remove(crawler);
    }
}
