using System;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[HarmonyPatch(typeof(ShipTeleporter), nameof(ShipTeleporter.PressTeleportButtonClientRpc))]
internal static class InverseTeleporterBots
{
    private static readonly MethodInfo? GetLanding = AccessTools.Method(typeof(ShipTeleporter), "GetInverseTelePosition");
    private static readonly MethodInfo? TeleportBot = AccessTools.Method(typeof(ShipTeleporter), "TeleportPlayerOutWithInverseTeleporter");
    private static readonly object?[] Arguments = new object?[2];

    [HarmonyPostfix]
    private static void IncludeServerOwnedBots(ShipTeleporter __instance)
    {
        if (!__instance.isInverseTeleporter || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer ||
            StartOfRound.Instance == null || StartOfRound.Instance.inShipPhase || GetLanding == null || TeleportBot == null)
            return;

        var players = StartOfRound.Instance.allPlayerScripts;
        Vector3 platform = __instance.teleportOutPosition.position;
        int teleported = 0;
        for (int index = 0; index < players.Length; index++)
        {
            PlayerControllerB player = players[index];
            if (player == null || !BotBody.Contains(player) || player.isPlayerDead || player.shipTeleporterId == 1 ||
                (player.transform.position - platform).sqrMagnitude > 4f)
                continue;

            try
            {
                Vector3 landing = (Vector3)GetLanding.Invoke(__instance, null)!;
                __instance.TeleportPlayerOutServerRpc(index, landing);
                Arguments[0] = index;
                Arguments[1] = landing;
                TeleportBot.Invoke(__instance, Arguments);
                teleported++;
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Could not send bot {player.playerClientId} through the inverse teleporter: {error.GetBaseException().Message}");
            }
        }

        if (teleported > 0)
            Plugin.Log.LogInfo($"Inverse teleporter sent {teleported} nearby crew bot(s) inside.");
    }
}
