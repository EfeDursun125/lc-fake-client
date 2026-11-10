using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalFakeClient;

internal static class ModelReplacementCompatibility
{
    private const string PluginId = "meow.ModelReplacementAPI";
    private static readonly FieldInfo SteamIdField = AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.playerSteamId));
    private static readonly MethodInfo ReadSteamIdMethod = AccessTools.Method(typeof(ModelReplacementCompatibility), nameof(ReadSteamId));
    private static Action<PlayerControllerB>? removeReplacement;

    internal static void Install(Harmony harmony)
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginId, out var info) || info.Instance == null)
            return;
        var api = info.Instance.GetType();
        var setReplacement = AccessTools.Method(api, "SetPlayerModelReplacement", new[] { typeof(PlayerControllerB), typeof(Type) });
        var remove = AccessTools.Method(api, "RemovePlayerModelReplacement", new[] { typeof(PlayerControllerB) });
        if (setReplacement == null || remove == null || SteamIdField == null || ReadSteamIdMethod == null)
        {
            Plugin.Log.LogWarning("ModelReplacementAPI is present, but its replacement methods are not compatible with bot models.");
            return;
        }
        try
        {
            harmony.Patch(setReplacement, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ModelReplacementCompatibility), nameof(Transpiler))));
            removeReplacement = (Action<PlayerControllerB>)Delegate.CreateDelegate(typeof(Action<PlayerControllerB>), remove);
            Plugin.Log.LogInfo("ModelReplacementAPI bot model support enabled.");
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not enable ModelReplacementAPI bot model support: {error.Message}");
        }
    }

    private static ulong ReadSteamId(PlayerControllerB player)
    {
        ulong steamId = player.playerSteamId;
        return steamId == 0 && BotBody.Contains(player) ? 1UL : steamId;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int changes = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, SteamIdField))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = ReadSteamIdMethod;
                changes++;
            }
            yield return instruction;
        }
        if (changes == 0)
            Plugin.Log.LogWarning("ModelReplacementAPI changed its Steam ID check; bot model support could not be applied.");
    }

    internal static void Remove(PlayerControllerB player)
    {
        if (removeReplacement == null || player == null)
            return;
        try
        {
            removeReplacement(player);
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not clean up a bot model replacement: {error.Message}");
        }
    }
}
