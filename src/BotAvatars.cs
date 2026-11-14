using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;

namespace LethalFakeClient;

internal static class BotAvatars
{
    private static readonly Dictionary<PlayerControllerB, ulong> ids = new Dictionary<PlayerControllerB, ulong>();
    private static readonly Dictionary<PlayerControllerB, ulong> selections = new Dictionary<PlayerControllerB, ulong>();
    private static readonly List<ulong> candidates = new List<ulong>();
    private static readonly HashSet<ulong> unavailable = new HashSet<ulong>();

    internal static ulong Choose(out string name)
    {
        name = string.Empty;
        if (!SteamClient.IsValid)
            return 0;
        candidates.Clear();
        unavailable.Clear();
        try
        {
            unavailable.Add(SteamClient.SteamId);
            foreach (ulong assigned in selections.Values)
                if (assigned != 0)
                    unavailable.Add(assigned);
            var round = StartOfRound.Instance;
            if (round != null)
            {
                foreach (int slot in round.ClientPlayerList.Values)
                    if ((uint)slot < (uint)round.allPlayerScripts.Length && round.allPlayerScripts[slot] != null)
                        unavailable.Add(round.allPlayerScripts[slot].playerSteamId);
            }
            foreach (var friend in SteamFriends.GetFriends())
            {
                ulong id = friend.Id;
                if (id != 0 && !friend.IsPlayingThisGame && !unavailable.Contains(id) && !candidates.Contains(id))
                    candidates.Add(id);
            }
            if (candidates.Count == 0)
                return 0;
            ulong selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            name = new Friend(selected).Name;
            return selected;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not select a Steam profile: {error.Message}");
            return 0;
        }
    }

    internal static void Reserve(PlayerControllerB player, ulong id)
    {
        if (player != null && id != 0)
            selections[player] = id;
    }

    internal static void Assign(PlayerControllerB player, ulong id)
    {
        if (player == null)
            return;
        ids[player] = id;
    }

    internal static ulong ProfileId(PlayerControllerB player)
    {
        return player == null ? 0UL : ids.TryGetValue(player, out ulong id) ? id : player.playerSteamId;
    }

    internal static void Remove(PlayerControllerB player)
    {
        if (player != null)
        {
            ids.Remove(player);
            selections.Remove(player);
        }
    }

    internal static void Clear()
    {
        ids.Clear();
        selections.Clear();
        candidates.Clear();
        unavailable.Clear();
    }
}

[HarmonyPatch(typeof(HUDManager), nameof(HUDManager.UpdateBoxesSpectateUI))]
internal static class BotSpectateAvatarPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var steamId = AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.playerSteamId));
        var profileId = AccessTools.Method(typeof(BotAvatars), nameof(BotAvatars.ProfileId));
        int changes = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, steamId))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = profileId;
                changes++;
            }
            yield return instruction;
        }
        if (changes == 0)
            Plugin.Log.LogWarning("Could not find the spectator profile image lookup; bot avatars are unavailable.");
    }
}
