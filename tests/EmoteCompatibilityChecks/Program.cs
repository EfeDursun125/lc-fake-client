using System;
using System.Collections.Generic;
using System.Reflection;
using LethalFakeClient;

var player = new GameNetcodeStuff.PlayerControllerB();
var mapper = new BoneMapper();
BoneMapper.playersToMappers[player.gameObject] = mapper;
BoneMapper.animClips["custom.wave"] = new CustomAnimationClip();
BoneMapper.animClips["custom.hidden"] = new CustomAnimationClip { animates = false };
EmotesAPI.CustomEmotesAPI.randomClipList.Add("custom.wave");
EmotesAPI.CustomEmotesAPI.randomClipList.Add("custom.hidden");
BetterEmote.Utils.Settings.EnabledList[3] = true;

UnityEngine.Random.Next = 0;
Check(EmoteCompatibility.Choose(player) == "better:3", "discovers enabled BetterEmotes animation");
UnityEngine.Random.Next = 1;
Check(EmoteCompatibility.Choose(player) == "api:custom.wave", "discovers registered API animation");
Check(EmoteCompatibility.BetterController("better:3") != null && EmoteCompatibility.BetterNumber("better:3") == 3, "resolves BetterEmotes controller");
Check(EmoteCompatibility.TryPlayApi(player, "api:custom.wave") && mapper.LastAnimation == "custom.wave", "plays API animation for the selected player");
EmoteCompatibility.StopApi(player);
Check(mapper.LastAnimation == "none", "stops API animation on the selected player");
LethalEmotesAPI.Utils.BlacklistSettings.emotesDisabled.Add("custom.wave");
Check(!EmoteCompatibility.TryPlayApi(player, "api:custom.wave"), "rejects disabled API animation");
UnityEngine.Random.Next = 1;
Check(EmoteCompatibility.Choose(player) == string.Empty, "disabled API animation is not chosen");
BetterEmote.Utils.Settings.DisableModelOverride = true;
UnityEngine.Random.Next = 0;
Check(EmoteCompatibility.Choose(player) == string.Empty, "falls back to vanilla when optional emotes are unavailable");
Console.WriteLine("All optional emote compatibility checks passed.");

static void Check(bool value, string description)
{
    if (!value)
        throw new InvalidOperationException(description);
    Console.WriteLine("PASS " + description);
}

namespace UnityEngine
{
    public sealed class GameObject { }
    public class RuntimeAnimatorController { }
    public static class Random
    {
        public static int Next;
        public static int Range(int min, int max) => Math.Clamp(Next, min, max - 1);
    }
}
namespace GameNetcodeStuff
{
    public sealed class PlayerControllerB
    {
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static Type? TypeByName(string name) => Assembly.GetExecutingAssembly().GetType(name);
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        public static MethodInfo? Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters);
    }
}
namespace LethalFakeClient
{
    internal static class Plugin
    {
        internal static readonly Logger Log = new Logger();
    }
    internal sealed class Logger
    {
        internal void LogWarning(string message) => Console.WriteLine(message);
    }
}
namespace BetterEmote.Patches
{
    internal static class EmotePatch
    {
        public static UnityEngine.RuntimeAnimatorController local = new UnityEngine.RuntimeAnimatorController();
    }
}
namespace BetterEmote.Utils
{
    internal static class Settings
    {
        public static bool[] EnabledList = new bool[11];
        public static bool DisableModelOverride;
    }
}
namespace EmotesAPI
{
    internal static class CustomEmotesAPI
    {
        public static List<string> randomClipList = new List<string>();
    }
}
namespace LethalEmotesAPI.Utils
{
    internal static class BlacklistSettings
    {
        public static List<string> emotesDisabled = new List<string>();
    }
}
internal sealed class BoneMapper
{
    public static Dictionary<UnityEngine.GameObject, BoneMapper> playersToMappers = new Dictionary<UnityEngine.GameObject, BoneMapper>();
    public static Dictionary<string, object> animClips = new Dictionary<string, object>();
    public string LastAnimation = string.Empty;
    public void PlayAnim(string name, int position) => LastAnimation = name;
}
internal sealed class CustomAnimationClip
{
    public bool animates = true;
}
