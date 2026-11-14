using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalFakeClient;

internal static class EmoteCompatibility
{
    private const string BetterPrefix = "better:";
    private const string ApiPrefix = "api:";
    private const int MaximumKeyBytes = 120;
    private static readonly List<string> choices = new List<string>();
    private static bool initialized;
    private static FieldInfo? betterController;
    private static FieldInfo? betterEnabled;
    private static FieldInfo? betterDisabled;
    private static FieldInfo? randomClips;
    private static FieldInfo? clips;
    private static FieldInfo? mappers;
    private static FieldInfo? disabledClips;
    private static FieldInfo? clipAnimates;
    private static MethodInfo? playAnimation;

    private static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        var better = AccessTools.TypeByName("BetterEmote.Patches.EmotePatch");
        var settings = AccessTools.TypeByName("BetterEmote.Utils.Settings");
        betterController = better != null ? AccessTools.Field(better, "local") : null;
        betterEnabled = settings != null ? AccessTools.Field(settings, "EnabledList") : null;
        betterDisabled = settings != null ? AccessTools.Field(settings, "DisableModelOverride") : null;
        var api = AccessTools.TypeByName("EmotesAPI.CustomEmotesAPI");
        var mapper = AccessTools.TypeByName("BoneMapper");
        var blacklist = AccessTools.TypeByName("LethalEmotesAPI.Utils.BlacklistSettings");
        var clip = AccessTools.TypeByName("CustomAnimationClip");
        randomClips = api != null ? AccessTools.Field(api, "randomClipList") : null;
        clips = mapper != null ? AccessTools.Field(mapper, "animClips") : null;
        mappers = mapper != null ? AccessTools.Field(mapper, "playersToMappers") : null;
        disabledClips = blacklist != null ? AccessTools.Field(blacklist, "emotesDisabled") : null;
        clipAnimates = clip != null ? AccessTools.Field(clip, "animates") : null;
        playAnimation = mapper != null ? AccessTools.Method(mapper, "PlayAnim", new[] { typeof(string), typeof(int) }) : null;
    }

    private static bool BetterAvailable()
    {
        return betterController?.GetValue(null) is RuntimeAnimatorController && !(betterDisabled?.GetValue(null) is bool disabled && disabled);
    }

    internal static string Choose(PlayerControllerB player)
    {
        Initialize();
        choices.Clear();
        if (BetterAvailable())
        {
            var enabled = betterEnabled?.GetValue(null) as bool[];
            for (int index = 3; index <= 9; index++)
                if (enabled == null || index >= enabled.Length || enabled[index])
                    choices.Add(BetterPrefix + index);
        }
        if (Mapper(player) != null && randomClips?.GetValue(null) is IList names && clips?.GetValue(null) is IDictionary registered)
        {
            var disabled = disabledClips?.GetValue(null) as IList;
            foreach (var candidate in names)
            {
                if (!(candidate is string name) || string.IsNullOrEmpty(name) || name.IndexOf("BetterEmotes__", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("TooManyEmotes__", StringComparison.OrdinalIgnoreCase) >= 0 || !registered.Contains(name) ||
                    registered[name] == null || clipAnimates?.GetValue(registered[name]) is bool animates && !animates ||
                    disabled != null && disabled.Contains(name) ||
                    Encoding.UTF8.GetByteCount(name) > MaximumKeyBytes - ApiPrefix.Length)
                    continue;
                choices.Add(ApiPrefix + name);
            }
        }
        int selected = UnityEngine.Random.Range(0, choices.Count + 2);
        return selected < choices.Count ? choices[selected] : string.Empty;
    }

    private static object? Mapper(PlayerControllerB player)
    {
        if (player == null || !(mappers?.GetValue(null) is IDictionary lookup) || !lookup.Contains(player.gameObject))
            return null;
        return lookup[player.gameObject];
    }

    internal static RuntimeAnimatorController? BetterController(string key)
    {
        Initialize();
        return key.StartsWith(BetterPrefix, StringComparison.Ordinal) && BetterAvailable()
            ? betterController!.GetValue(null) as RuntimeAnimatorController : null;
    }

    internal static int BetterNumber(string key)
    {
        if (!key.StartsWith(BetterPrefix, StringComparison.Ordinal) || !int.TryParse(key.Substring(BetterPrefix.Length), out int value))
            return 0;
        return value >= 3 && value <= 9 ? value : 0;
    }

    internal static bool IsApi(string key) => key.StartsWith(ApiPrefix, StringComparison.Ordinal);

    internal static bool TryPlayApi(PlayerControllerB player, string key)
    {
        Initialize();
        if (!IsApi(key) || playAnimation == null)
            return false;
        string name = key.Substring(ApiPrefix.Length);
        if (!(clips?.GetValue(null) is IDictionary registered) || !registered.Contains(name) || registered[name] == null ||
            disabledClips?.GetValue(null) is IList disabled && disabled.Contains(name))
            return false;
        object? mapper = Mapper(player);
        if (mapper == null)
            return false;
        try
        {
            playAnimation.Invoke(mapper, new object[] { name, -1 });
            return true;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not play a bot API emote: {error.GetBaseException().Message}");
            return false;
        }
    }

    internal static void StopApi(PlayerControllerB player)
    {
        object? mapper = Mapper(player);
        if (mapper == null || playAnimation == null)
            return;
        try
        {
            playAnimation.Invoke(mapper, new object[] { "none", -1 });
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not stop a bot API emote: {error.GetBaseException().Message}");
        }
    }
}
