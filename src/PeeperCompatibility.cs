using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalFakeClient;

internal static class PeeperCompatibility
{
    private static System.Type? peeperType;
    private static FieldInfo? attachedField;
    private static FieldInfo? playerField;
    private static MethodInfo? ejectMethod;
    private static bool resolved;
    private static float nextResolve;

    private static void Resolve()
    {
        if (resolved || Time.unscaledTime < nextResolve)
            return;
        nextResolve = Time.unscaledTime + 10f;
        peeperType = AccessTools.TypeByName("LCPeeper.PeeperAI");
        if (peeperType == null)
            return;
        resolved = true;
        attachedField = AccessTools.Field(peeperType, "isAttached");
        playerField = AccessTools.Field(peeperType, "attachedPlayer");
        ejectMethod = AccessTools.Method(peeperType, "EjectFromPlayerServerRpc");
    }

    internal static PlayerControllerB? AttachedPlayer(Component source)
    {
        Resolve();
        if (peeperType == null || source == null || !peeperType.IsInstanceOfType(source) ||
            attachedField == null || playerField == null || !(attachedField.GetValue(source) is bool attached) || !attached)
            return null;
        return playerField.GetValue(source) as PlayerControllerB;
    }

    internal static bool HasAttached(PlayerControllerB player)
    {
        foreach (var enemy in Blackboard.Enemies)
            if (enemy != null && !enemy.isEnemyDead && AttachedPlayer(enemy) == player)
                return true;
        return false;
    }

    internal static void EjectFromBot(PlayerControllerB player)
    {
        foreach (var enemy in Blackboard.Enemies)
            if (enemy != null && !enemy.isEnemyDead && AttachedPlayer(enemy) == player)
                ejectMethod?.Invoke(enemy, new object[] { player.playerClientId });
    }
}
