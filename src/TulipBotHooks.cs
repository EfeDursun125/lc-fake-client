using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch]
internal static class TulipBotHooks
{
    private static readonly Action<FlowerSnakeEnemy> Conductor =
        AccessTools.MethodDelegate<Action<FlowerSnakeEnemy>>(AccessTools.Method(typeof(FlowerSnakeEnemy), "MainSnakeActAsConductor"));

    private static PlayerControllerB BotOrLocalPlayer(GameNetworkManager manager, FlowerSnakeEnemy snake)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && snake.clingingToPlayer != null &&
            BotBody.Contains(snake.clingingToPlayer))
            return snake.clingingToPlayer;
        return manager.localPlayerController;
    }

    [HarmonyPatch(typeof(FlowerSnakeEnemy), "MainSnakeActAsConductor")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> BotConductor(IEnumerable<CodeInstruction> instructions)
    {
        var localPlayer = AccessTools.Field(typeof(GameNetworkManager), nameof(GameNetworkManager.localPlayerController));
        var resolve = AccessTools.Method(typeof(TulipBotHooks), nameof(BotOrLocalPlayer));
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, localPlayer))
            {
                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, resolve);
                replacements++;
            }
            else
                yield return instruction;
        }
        if (replacements == 0)
            Plugin.Log.LogWarning("Tulip Snake bot flight support could not find the local player check.");
    }

    [HarmonyPatch(typeof(FlowerSnakeEnemy), nameof(FlowerSnakeEnemy.Update))]
    [HarmonyPostfix]
    private static void UpdateBotSnake(FlowerSnakeEnemy __instance)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || __instance.clingPosition != 4 ||
            __instance.clingingToPlayer == null || !BotBody.TryGet(__instance.clingingToPlayer, out var body))
            return;
        var player = __instance.clingingToPlayer;
        var round = StartOfRound.Instance;
        if (round == null)
            return;
        if (body.Dead || player.isPlayerDead || __instance.isEnemyDead || __instance.daytimeEnemyLeaving ||
            player.isInElevator && round.shipIsLeaving)
        {
            __instance.StopClingingServerRpc((int)player.playerClientId);
            return;
        }
        if (round.livingPlayers <= 0)
            Conductor(__instance);
    }
}
