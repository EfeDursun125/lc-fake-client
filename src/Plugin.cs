using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

[BepInPlugin("dev.lethalfakeclient", "Lethal Fake Client", "0.9.54")]
[BepInDependency("meow.ModelReplacementAPI", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("BetterEmotes", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.weliveinasociety.CustomEmotesAPI", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log = null!;
    private static Session? session;
    private static NetworkManager? discoveredNetwork;
    private static float nextNetworkDiscovery;
    private static int lastPumpFrame = -1;
    private static bool loggedPump;
    private static BotDriver? driver;

    private void Awake()
    {
        Log = Logger;
        BotSettings.Bind(Config);
        var harmony = new Harmony("dev.lethalfakeclient");
        harmony.PatchAll(typeof(Plugin).Assembly);
        ModelReplacementCompatibility.Install(harmony);
        try
        {
            BotEnemyCollisionContext.PatchModded(harmony);
        }
        catch (System.Exception error)
        {
            Log.LogWarning($"Some modded enemy collision support could not be loaded: {error.Message}");
        }
        if (driver == null)
        {
            var driverObject = new GameObject("LethalFakeClient.Driver");
            UnityEngine.Object.DontDestroyOnLoad(driverObject);
            driver = driverObject.AddComponent<BotDriver>();
        }
        var patchedUpdate = Harmony.GetPatchInfo(AccessTools.Method(typeof(StartOfRound), "Update"));
        Log.LogInfo($"Bot game update hook installed={patchedUpdate != null && patchedUpdate.Owners.Contains("dev.lethalfakeclient")}.");
        Log.LogInfo("Adaptive AI Director loaded.");
    }

    private void Update() => Pump(false);

    internal static void Pump(bool fromGame)
    {
        if (lastPumpFrame == Time.frameCount)
            return;
        lastPumpFrame = Time.frameCount;
        if (!loggedPump)
        {
            loggedPump = true;
            Log.LogInfo($"Bot update loop started (source={(fromGame ? "game" : "plugin")}).");
        }
        BotEnemyCollisionContext.PatchPending();
        TickSession();
    }

    private static void TickSession()
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsListening)
        {
            if (Time.realtimeSinceStartup >= nextNetworkDiscovery)
            {
                nextNetworkDiscovery = Time.realtimeSinceStartup + 2f;
                discoveredNetwork = UnityEngine.Object.FindObjectOfType<NetworkManager>();
            }
            if (discoveredNetwork != null && discoveredNetwork.IsListening)
                network = discoveredNetwork;
        }
        if (network == null || !network.IsListening || network.ShutdownInProgress)
        {
            CloseSession();
            return;
        }

        if (session == null || session.Network != network)
        {
            CloseSession();
            session = new Session(network);
            Log.LogInfo($"Bot network session started (host={network.IsHost}, server={network.IsServer}, client={network.IsClient}).");
        }

        session.Tick();
    }

    private static void CloseSession()
    {
        if (session != null)
            BotAvatars.Clear();
        session?.Dispose();
        session = null;
    }


    private void OnDestroy() => Log.LogInfo("BepInEx plugin component destroyed; bot driver remains active.");

    internal static void DriverDestroyed()
    {
        driver = null;
        BotEnemyCollisionContext.StopPatching();
        CloseSession();
    }

    internal static void BeforeTeleport(PlayerControllerB player)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && player.shipTeleporterId == 1 &&
            BotBody.TryGet(player, out var body))
            body.DropAll();
    }

    internal static void AfterTeleport(PlayerControllerB player, Vector3 position) => session?.BotTeleported(player, position);

    internal static bool BotsSurvivedDeparture => session?.BotsSurvivedDeparture == true;

    internal static void BeforeRevive() => session?.BeforeRevive();

    internal static void HumanDroppedItem(GrabbableObject item) => session?.HumanDroppedItem(item);
    internal static void DeliveryChanged() => session?.DeliveryChanged();

    internal static void SyncBotEmote(PlayerControllerB player, string emote) => session?.SendBotEmote(player, emote);

    internal static void SyncBotDroppedItem(PlayerControllerB player, GrabbableObject item, Vector3 localFloor, bool aboard, bool inside)
        => session?.SendDroppedItem(player, item, localFloor, aboard, inside);

    internal static void Landing() => session?.PrepareRound();

    internal static void BeforeJoin(ulong clientId)
    {
        if (clientId != NetworkManager.ServerClientId)
        {
            session?.ReleaseRetired();
            session?.ClearTeam();
        }
    }
}

internal sealed class BotDriver : MonoBehaviour
{
    private void Update() => Plugin.Pump(false);

    private void OnDestroy() => Plugin.DriverDestroyed();
}

[HarmonyPatch]
internal static class GameHooks
{
    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ReviveDeadPlayers))]
    [HarmonyPrefix]
    private static void BeforePlayersRevive() => Plugin.BeforeRevive();
    [HarmonyPatch(typeof(StartOfRound), "Update")]
    [HarmonyPostfix]
    private static void PumpFromGame() => Plugin.Pump(true);

    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SetShipDoorsClosed))]
    [HarmonyPostfix]
    private static void ShipDoorAnimation(bool closed)
    {
        var door = UnityEngine.Object.FindObjectOfType<HangarShipDoor>();
        if (door != null)
            door.PlayDoorAnimation(closed);
    }
    [HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc")]
    [HarmonyPostfix]
    private static void Landing() => Plugin.Landing();

    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnClientConnect))]
    [HarmonyPrefix]
    private static void ClientConnect(ulong clientId) => Plugin.BeforeJoin(clientId);

    [HarmonyPatch(typeof(EnemyAI), "Start")]
    [HarmonyPostfix]
    private static void EnemySpawned(EnemyAI __instance) => Blackboard.Register(__instance);

    [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.OnDestroy))]
    [HarmonyPrefix]
    private static void EnemyDestroyed(EnemyAI __instance)
    {
        ThumperBotCollision.Forget(__instance);
        Blackboard.Remove(__instance);
    }

    [HarmonyPatch(typeof(GrabbableObject), "Start")]
    [HarmonyPostfix]
    private static void ItemSpawned(GrabbableObject __instance) => Blackboard.Register(__instance);

    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.OnDestroy))]
    [HarmonyPrefix]
    private static void ItemDestroyed(GrabbableObject __instance) => Blackboard.Remove(__instance);

    [HarmonyPatch(typeof(PlayerControllerB), "ThrowObjectServerRpc")]
    [HarmonyPrefix]
    private static void HumanDropped(PlayerControllerB __instance, NetworkObjectReference grabbedObject)
    {
        if (!__instance.IsServer || BotBody.Contains(__instance) || !grabbedObject.TryGet(out var networkObject) || networkObject == null)
            return;
        var item = networkObject.GetComponent<GrabbableObject>();
        if (item != null)
            Plugin.HumanDroppedItem(item);
    }

    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.EndOfGameClientRpc))]
    [HarmonyPrefix]
    private static void PreserveBotScrap(StartOfRound __instance, ref int scrapCollectedOnServer)
    {
        if (__instance.IsServer && __instance.livingPlayers <= 0 && Plugin.BotsSurvivedDeparture)
        {
            scrapCollectedOnServer = __instance.GetValueOfAllScrap(true, true);
            __instance.scrapCollectedLastRound = scrapCollectedOnServer;
        }
    }

    [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipLeaveAutomatically))]
    [HarmonyPrefix]
    private static bool KeepRoundForLivingBots(StartOfRound __instance, bool leavingOnMidnight)
    {
        if (leavingOnMidnight || __instance.livingPlayers > 0)
            return true;
        foreach (var player in BotBody.Players)
        {
            if (player != null && player.isPlayerControlled && !player.isPlayerDead &&
                BotBody.TryGet(player, out var body) && !body.Dead)
            {
                if (RoundSurvivalPolicy.ContinueAfterHumanDeaths(leavingOnMidnight, __instance.livingPlayers, true))
                {
                    __instance.allPlayersDead = false;
                    return false;
                }
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(PlayerControllerB), "OnEnable")]
    [HarmonyPrefix]
    private static bool EnablePlayer(PlayerControllerB __instance) => !BotBody.Contains(__instance);

    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    [HarmonyPrefix]
    private static bool TickPlayer(PlayerControllerB __instance) => !BotBody.Contains(__instance);

    [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
    [HarmonyPrefix]
    private static bool LateTickPlayer(PlayerControllerB __instance) => !BotBody.Contains(__instance);

    [HarmonyPatch(typeof(PlayerControllerB), "SwitchToItemSlot")]
    [HarmonyPrefix]
    private static bool BotInventory(PlayerControllerB __instance, int slot, GrabbableObject? fillSlotWithItem)
    {
        if (!BotBody.Contains(__instance))
            return true;
        if ((uint)slot >= (uint)__instance.ItemSlots.Length && slot != 50)
            return false;

        using var hudScope = BotHudScope.Enter();
        var previous = __instance.currentlyHeldObjectServer;
        if (previous != null && previous != fillSlotWithItem)
            previous.PocketItem();
        __instance.currentItemSlot = slot;
        if (fillSlotWithItem != null)
        {
            if (slot == 50)
                __instance.ItemOnlySlot = fillSlotWithItem;
            else
                __instance.ItemSlots[slot] = fillSlotWithItem;
        }
        __instance.currentlyHeldObjectServer = slot == 50 ? __instance.ItemOnlySlot : __instance.ItemSlots[slot];
        if (__instance.currentlyHeldObjectServer != null)
        {
            __instance.currentlyHeldObjectServer.playerHeldBy = __instance;
            __instance.currentlyHeldObjectServer.parentObject = __instance.serverItemHolder;
            __instance.currentlyHeldObjectServer.EquipItem();
            __instance.isHoldingObject = true;
            __instance.twoHanded = __instance.currentlyHeldObjectServer.itemProperties.twoHanded;
            __instance.twoHandedAnimation = __instance.currentlyHeldObjectServer.itemProperties.twoHandedAnimation;
        }
        else
        {
            __instance.isHoldingObject = false;
            __instance.twoHanded = false;
            __instance.twoHandedAnimation = false;
        }
        return false;
    }

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.TeleportPlayer))]
    [HarmonyPrefix]
    private static void BeforePlayerTeleport(PlayerControllerB __instance) => Plugin.BeforeTeleport(__instance);

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.TeleportPlayer))]
    [HarmonyPostfix]
    private static void AfterPlayerTeleport(PlayerControllerB __instance, Vector3 pos) => Plugin.AfterTeleport(__instance, pos);

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer))]
    [HarmonyPrefix]
    private static bool Damage(PlayerControllerB __instance, int damageNumber, CauseOfDeath causeOfDeath, int deathAnimation, Vector3 force)
    {
        if (!BotBody.TryGet(__instance, out var body))
            return true;
        body.ApplyDamage(damageNumber, causeOfDeath, deathAnimation, force, deathAnimation != -1);
        return false;
    }

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer))]
    [HarmonyPrefix]
    private static bool Kill(PlayerControllerB __instance, Vector3 bodyVelocity, bool spawnBody, CauseOfDeath causeOfDeath, int deathAnimation)
    {
        if (!BotBody.TryGet(__instance, out var body))
            return true;
        body.ApplyDamage(int.MaxValue, causeOfDeath, deathAnimation, bodyVelocity, spawnBody);
        return false;
    }
    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.JumpToFearLevel))]
    [HarmonyPrefix]
    private static bool Fear(PlayerControllerB __instance) => !BotBody.Contains(__instance);

}

[HarmonyPatch(typeof(StunGrenadeItem), "SetControlTipForGrenade")]
internal static class BotGrenadeHud
{
    private static bool Prefix(StunGrenadeItem __instance)
    {
        return __instance.playerHeldBy == null || !BotBody.Contains(__instance.playerHeldBy);
    }
}

[HarmonyPatch(typeof(ShotgunItem), "SetSafetyControlTip")]
internal static class BotShotgunHud
{
    private static bool Prefix(ShotgunItem __instance)
    {
        return __instance.playerHeldBy == null || !BotBody.Contains(__instance.playerHeldBy);
    }
}

[HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DestroyItemInSlot))]
internal static class BotAmmoConsumption
{
    private static bool Prefix(PlayerControllerB __instance, int itemSlot)
    {
        if (!BotBody.Contains(__instance))
            return true;
        if (itemSlot != 50 && (uint)itemSlot >= (uint)__instance.ItemSlots.Length)
            return false;
        var item = itemSlot == 50 ? __instance.ItemOnlySlot : __instance.ItemSlots[itemSlot];
        if (!(item is GunAmmo))
            return true;
        if (itemSlot == 50)
            __instance.ItemOnlySlot = null;
        else
            __instance.ItemSlots[itemSlot] = null;
        __instance.carryWeight = Mathf.Clamp(__instance.carryWeight - (item.itemProperties.weight - 1f), 1f, 10f);
        if (__instance.IsServer && item.NetworkObject != null && item.IsSpawned)
            item.NetworkObject.Despawn();
        return false;
    }
}

[HarmonyPatch(typeof(ShipTeleporter), "SetPlayerTeleporterId")]
internal static class BotTeleporterCapacity
{
    private static void Prefix(PlayerControllerB playerScript, ref int[] ___playersBeingTeleported)
    {
        if (playerScript == null || ___playersBeingTeleported == null || playerScript.playerClientId < (ulong)___playersBeingTeleported.Length ||
            playerScript.playerClientId >= (ulong)StartOfRound.Instance.allPlayerScripts.Length)
            return;
        int oldLength = ___playersBeingTeleported.Length;
        System.Array.Resize(ref ___playersBeingTeleported, StartOfRound.Instance.allPlayerScripts.Length);
        for (int index = oldLength; index < ___playersBeingTeleported.Length; index++)
            ___playersBeingTeleported[index] = -1;
    }
}

[HarmonyPatch(typeof(QuickMenuManager))]
internal static class BotQuickMenu
{
    [HarmonyPatch(nameof(QuickMenuManager.OpenQuickMenu))]
    [HarmonyPostfix]
    private static void ShowBots(QuickMenuManager __instance)
    {
        var round = StartOfRound.Instance;
        if (round == null || __instance.playerListSlots == null)
            return;
        foreach (var bot in BotBody.Players)
        {
            if (bot == null || !bot.isPlayerControlled || bot.isPlayerDead || bot.playerClientId >= (ulong)__instance.playerListSlots.Length ||
                round.ClientPlayerList.ContainsValue((int)bot.playerClientId))
                continue;
            var slot = __instance.playerListSlots[(int)bot.playerClientId];
            if (slot == null)
                continue;
            slot.slotContainer.SetActive(true);
            slot.usernameHeader.text = bot.playerUsername;
            slot.isConnected = true;
            slot.KickUserButton.SetActive(false);
            slot.volumeSliderContainer.SetActive(false);
            __instance.playerListPanel.SetActive(true);
        }
    }

    [HarmonyPatch(nameof(QuickMenuManager.CloseQuickMenu))]
    [HarmonyPostfix]
    private static void HideBots(QuickMenuManager __instance)
    {
        var round = StartOfRound.Instance;
        if (round == null || __instance.playerListSlots == null)
            return;
        foreach (var bot in BotBody.Players)
        {
            if (bot == null || bot.playerClientId >= (ulong)__instance.playerListSlots.Length ||
                round.ClientPlayerList.ContainsValue((int)bot.playerClientId))
                continue;
            var slot = __instance.playerListSlots[(int)bot.playerClientId];
            if (slot == null)
                continue;
            slot.isConnected = false;
            slot.slotContainer.SetActive(false);
        }
    }
}
