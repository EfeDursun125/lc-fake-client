using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

internal static class EnemyBotContext
{
    internal static PlayerControllerB? Current;
    internal static EnemyAI? CurrentEnemy;

    internal readonly struct State
    {
        internal readonly PlayerControllerB? Player;
        internal readonly EnemyAI? Enemy;

        internal State(PlayerControllerB? player, EnemyAI? enemy)
        {
            Player = player;
            Enemy = enemy;
        }
    }

    internal static PlayerControllerB LocalPlayer(GameNetworkManager manager)
    {
        return Current != null ? Current : manager.localPlayerController;
    }
}

[HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.MeetsStandardPlayerCollisionConditions))]
internal static class BotCollisionConditions
{
    private static readonly Func<EnemyAI, Vector3, bool> Separated =
        AccessTools.MethodDelegate<Func<EnemyAI, Vector3, bool>>(AccessTools.Method(typeof(EnemyAI), "IsSeparatedByMineshaftElevator"));

    [HarmonyPrefix]
    private static bool Prefix(EnemyAI __instance, Collider other, bool inKillAnimation,
        bool overrideIsInsideFactoryCheck, ref PlayerControllerB __result)
    {
        var player = other != null ? other.GetComponent<PlayerControllerB>() : null;
        if (player == null || !BotBody.Contains(player))
            return true;
        __result = null!;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && EnemyBotContext.Current == player &&
            !__instance.isEnemyDead && __instance.ventAnimationFinished && !inKillAnimation &&
            __instance.stunNormalizedTimer < 0f && __instance.PlayerIsTargetable(player, false, overrideIsInsideFactoryCheck) &&
            !Separated(__instance, player.transform.position))
            __result = player;
        return false;
    }
}

[HarmonyPatch]
internal static class BotEnemyCollisionContext
{
    private static readonly FieldInfo LocalPlayerField = AccessTools.Field(typeof(GameNetworkManager), nameof(GameNetworkManager.localPlayerController));
    private static readonly MethodInfo ResolveLocalPlayer = AccessTools.Method(typeof(EnemyBotContext), nameof(EnemyBotContext.LocalPlayer));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var methods = FindCollisionMethods(typeof(EnemyAI).Assembly);
        if (methods.Count == 0)
            Plugin.Log.LogWarning("Could not find vanilla enemy collision methods for bot support.");
        return methods;
    }

    private static List<MethodBase> FindCollisionMethods(Assembly assembly)
    {
        var methods = new List<MethodBase>();
        try
        {
            foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
            {
                try
                {
                    if (type == null || type.ContainsGenericParameters || !typeof(EnemyAI).IsAssignableFrom(type))
                        continue;
                    var method = type.GetMethod(nameof(EnemyAI.OnCollideWithPlayer), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(Collider) }, null);
                    if (method != null && !method.IsAbstract && method.GetMethodBody() != null)
                        methods.Add(method);
                }
                catch (Exception error)
                {
                    Plugin.Log.LogDebug($"Skipping unloadable enemy type in {assembly.GetName().Name}: {error.GetType().Name}");
                }
            }
        }
        catch (Exception error)
        {
            Plugin.Log.LogDebug($"Skipping unloadable assembly {assembly.GetName().Name}: {error.GetType().Name}");
        }
        return methods;
    }

    internal static void PatchModded(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(BotEnemyCollisionContext), nameof(Prefix)));
        var finalizer = new HarmonyMethod(AccessTools.Method(typeof(BotEnemyCollisionContext), nameof(Finalizer)));
        var transpiler = new HarmonyMethod(AccessTools.Method(typeof(BotEnemyCollisionContext), nameof(Transpiler)));
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly == typeof(EnemyAI).Assembly || assembly == typeof(Plugin).Assembly)
                continue;
            foreach (var method in FindCollisionMethods(assembly))
            {
                try
                {
                    harmony.Patch(method, prefix: prefix, finalizer: finalizer, transpiler: transpiler);
                }
                catch (Exception error)
                {
                    Plugin.Log.LogWarning($"Could not add bot collision support for {method.DeclaringType?.FullName}: {error.Message}");
                }
            }
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(EnemyAI __instance, Collider __0, ref EnemyBotContext.State __state)
    {
        __state = new EnemyBotContext.State(EnemyBotContext.Current, EnemyBotContext.CurrentEnemy);
        var player = __0 != null ? __0.GetComponent<PlayerControllerB>() : null;
        if (player == null || !BotBody.Contains(player))
        {
            EnemyBotContext.Current = null;
            EnemyBotContext.CurrentEnemy = null;
            return true;
        }
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return false;
        EnemyBotContext.Current = player;
        EnemyBotContext.CurrentEnemy = __instance;
        return true;
    }

    [HarmonyFinalizer]
    private static void Finalizer(EnemyBotContext.State __state)
    {
        EnemyBotContext.Current = __state.Player;
        EnemyBotContext.CurrentEnemy = __state.Enemy;
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, LocalPlayerField))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = ResolveLocalPlayer;
            }
            yield return instruction;
        }
    }
}

[HarmonyPatch(typeof(MouthDogAI), "ChaseLocalPlayer")]
internal static class BotDogChase
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return BotEnemyCollisionContext.Transpiler(instructions);
    }
}
