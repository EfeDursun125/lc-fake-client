using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class Session : IDisposable
{
    private const string HelloMessage = "LethalFakeClient.Director.v2.Hello";
    private const string StateMessage = "LethalFakeClient.Director.v2.State";
    private const string DeathMessage = "LethalFakeClient.Director.v2.Death";
    private const string ViewMessage = "LethalFakeClient.Director.v3.View";
    private const string DropMessage = "LethalFakeClient.Director.v4.Drop";
    private const string EmoteMessage = "LethalFakeClient.Director.v10.Emote";
    private const byte ProtocolVersion = 10;
    private const int MaxBots = 128;
    private const int RecordBytes = sizeof(int) + sizeof(float) * 7 + sizeof(byte) * 2 + sizeof(ulong);
    private readonly List<CrewBot> bots = new List<CrewBot>();
    private readonly List<ulong> recipients = new List<ulong>();
    private readonly HashSet<ulong> peers = new HashSet<ulong>();
    private readonly HashSet<PlayerControllerB> retired = new HashSet<PlayerControllerB>();
    private readonly Dictionary<int, Replica> replicas = new Dictionary<int, Replica>();
    private readonly Dictionary<int, string> pendingEmotes = new Dictionary<int, string>();
    private readonly Dictionary<ulong, (Vector3 Position, float Received, bool Valid)> observerViews = new Dictionary<ulong, (Vector3, float, bool)>();
    private readonly List<int> expired = new List<int>();
    private readonly HashSet<int> received = new HashSet<int>();
    private readonly Learning? learning;
    private Director? director;
    private bool roundPrepared;
    private bool needsFill;
    private bool hadBotsThisRound;
    private bool departureStarted;
    internal bool BotsSurvivedDeparture { get; private set; }
    private float nextReconcile;
    private float nextSpawnWarning;
    private float nextPreparationWarning;
    private float nextPeerWarning;
    private float nextPacket;
    private float lastPacket;
    private float nextViewPacket;
    private float nextStartupDiagnostic;
    private string? lastStartupDiagnostic;

    internal NetworkManager Network { get; }

    private sealed class Replica
    {
        internal readonly BotBody Body;
        internal Vector3 Point;
        internal Quaternion Rotation;
        internal byte Flags;
        internal bool Jumping;
        internal byte Emote;
        internal Vector3 LookPoint;
        internal bool ShipAnchored;
        internal Vector3 ShipLocalPosition;
        internal Quaternion ShipLocalRotation;
        internal Vector3 ShipLocalLook;

        internal Replica(BotBody body, Vector3 point, Quaternion rotation, byte flags)
        {
            Body = body;
            Point = point;
            Rotation = rotation;
            Flags = flags;
            body.Pose(point, rotation, false, (flags & 2) != 0, (flags & 4) != 0);
        }
    }

    internal Session(NetworkManager network)
    {
        Network = network;
        if (network.IsHost)
        {
            learning = new Learning();
            network.CustomMessagingManager.RegisterNamedMessageHandler(HelloMessage, ReceiveHello);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ViewMessage, ReceiveView);
            network.OnClientDisconnectCallback += Disconnected;
        }
        else
        {
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            network.CustomMessagingManager.RegisterNamedMessageHandler(DeathMessage, ReceiveDeath);
            network.CustomMessagingManager.RegisterNamedMessageHandler(DropMessage, ReceiveDroppedItem);
            network.CustomMessagingManager.RegisterNamedMessageHandler(EmoteMessage, ReceiveBotEmote);
        }
    }

    internal void SendBotEmote(PlayerControllerB player, string emote)
    {
        if (!Network.IsHost || !Network.IsListening || Network.ShutdownInProgress)
            return;
        recipients.Clear();
        foreach (ulong client in Network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && peers.Contains(client))
                recipients.Add(client);
        if (recipients.Count == 0)
            return;
        using var packet = new FastBufferWriter(sizeof(int) + 128, Allocator.Temp);
        packet.WriteValueSafe((int)player.playerClientId);
        var name = new FixedString128Bytes(emote);
        packet.WriteValueSafe(name);
        Network.CustomMessagingManager.SendNamedMessage(EmoteMessage, recipients, packet, NetworkDelivery.ReliableSequenced);
    }

    private void ReceiveBotEmote(ulong sender, FastBufferReader packet)
    {
        var round = StartOfRound.Instance;
        if (sender != NetworkManager.ServerClientId || round == null || !packet.TryBeginRead(sizeof(int)))
            return;
        packet.ReadValueSafe(out int slot);
        if ((uint)slot >= (uint)round.allPlayerScripts.Length || round.ClientPlayerList.ContainsValue(slot))
            return;
        var player = round.allPlayerScripts[slot];
        if (player == null || player.isPlayerDead || retired.Contains(player))
            return;
        packet.ReadValueSafe(out FixedString128Bytes name);
        string emote = name.ToString();
        if (emote.Length == 0)
            pendingEmotes.Remove(slot);
        else
            pendingEmotes[slot] = emote;
        if (replicas.TryGetValue(slot, out var replica))
            replica.Body.SetCustomEmote(emote);
    }

    internal void SendDroppedItem(PlayerControllerB player, GrabbableObject item, Vector3 localFloor, bool aboard, bool inside)
    {
        if (!Network.IsHost || !Network.IsListening || Network.ShutdownInProgress || item == null || !item.IsSpawned)
            return;
        recipients.Clear();
        foreach (ulong client in Network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && peers.Contains(client))
                recipients.Add(client);
        if (recipients.Count == 0)
            return;
        using var packet = new FastBufferWriter(sizeof(int) + sizeof(ulong) + sizeof(float) * 3 + sizeof(byte), Allocator.Temp);
        packet.WriteValueSafe((int)player.playerClientId);
        packet.WriteValueSafe(item.NetworkObjectId);
        packet.WriteValueSafe(localFloor);
        packet.WriteValueSafe((byte)((aboard ? 1 : 0) | (inside ? 2 : 0)));
        Network.CustomMessagingManager.SendNamedMessage(DropMessage, recipients, packet, NetworkDelivery.ReliableSequenced);
    }

    private void ReceiveDroppedItem(ulong sender, FastBufferReader packet)
    {
        var round = StartOfRound.Instance;
        if (sender != NetworkManager.ServerClientId || round == null ||
            !packet.TryBeginRead(sizeof(int) + sizeof(ulong) + sizeof(float) * 3 + sizeof(byte)))
            return;
        packet.ReadValueSafe(out int slot);
        packet.ReadValueSafe(out ulong objectId);
        packet.ReadValueSafe(out Vector3 floor);
        packet.ReadValueSafe(out byte flags);
        if ((uint)slot >= (uint)round.allPlayerScripts.Length ||
            !Network.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var networkObject) ||
            networkObject == null || !networkObject.TryGetComponent<GrabbableObject>(out var item))
            return;
        var player = round.allPlayerScripts[slot];
        if (player == null)
            return;
        bool aboard = (flags & 1) != 0;
        var heldBeforeDrop = player.currentlyHeldObjectServer;
        player.SetObjectAsNoLongerHeld(aboard, aboard, floor, item);
        item.isInFactory = (flags & 2) != 0;
        if (item.itemProperties != null && !item.itemProperties.syncDiscardFunction)
            item.playerHeldBy = null;
        if (player.currentlyHeldObjectServer == item)
            player.currentlyHeldObjectServer = null;
        else if (heldBeforeDrop != null)
        {
            player.isHoldingObject = true;
            player.twoHanded = heldBeforeDrop.itemProperties != null && heldBeforeDrop.itemProperties.twoHanded;
            player.twoHandedAnimation = heldBeforeDrop.itemProperties != null && heldBeforeDrop.itemProperties.twoHandedAnimation;
        }
    }

    internal void PrepareRound()
    {
        if (!Network.IsHost || roundPrepared)
            return;
        roundPrepared = true;
        departureStarted = false;
        BotsSurvivedDeparture = false;
        needsFill = true;
        Plugin.Log.LogInfo("Bot crew preparation requested after level generation.");
    }

    private void ReceiveHello(ulong sender, FastBufferReader reader)
    {
        if (Network.ConnectedClients.ContainsKey(sender) && reader.TryBeginRead(sizeof(byte)))
        {
            reader.ReadValueSafe(out byte version);
            if (version != ProtocolVersion || !peers.Add(sender))
                return;
            needsFill = true;
        }
    }

    private void Disconnected(ulong client)
    {
        peers.Remove(client);
        observerViews.Remove(client);
        needsFill = true;
    }

    private static bool TryLocalView(out Vector3 position)
    {
        position = default;
        var round = StartOfRound.Instance;
        if (round == null || round.localPlayerController == null || round.localPlayerController.isFreeCamera)
            return false;
        var camera = round.activeCamera;
        if (camera == null || !camera.isActiveAndEnabled)
            camera = Camera.main;
        if (camera == null || !camera.isActiveAndEnabled)
            return false;
        position = camera.transform.position;
        return true;
    }

    private void ReceiveView(ulong sender, FastBufferReader reader)
    {
        if (!Network.ConnectedClients.ContainsKey(sender) || !reader.TryBeginRead(sizeof(byte) + sizeof(float) * 3))
            return;
        reader.ReadValueSafe(out byte valid);
        reader.ReadValueSafe(out Vector3 position);
        bool finite = !float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z) &&
            !float.IsInfinity(position.x) && !float.IsInfinity(position.y) && !float.IsInfinity(position.z);
        observerViews[sender] = (position, Time.realtimeSinceStartup, valid != 0 && finite);
    }

    private void SendView(float now)
    {
        if (!Network.IsConnectedClient || now < nextViewPacket)
            return;
        nextViewPacket = now + 0.1f;
        bool valid = TryLocalView(out var position);
        using var packet = new FastBufferWriter(sizeof(byte) + sizeof(float) * 3, Allocator.Temp);
        packet.WriteValueSafe((byte)(valid ? 1 : 0));
        packet.WriteValueSafe(position);
        Network.CustomMessagingManager.SendNamedMessage(ViewMessage, NetworkManager.ServerClientId, packet, NetworkDelivery.UnreliableSequenced);
    }

    private bool HiddenFromHumanViews(Vector3 point)
    {
        var round = StartOfRound.Instance;
        if (round == null || !Network.IsHost || Network.ConnectedClientsIds.Count == 0)
            return false;
        foreach (ulong client in Network.ConnectedClientsIds)
        {
            Vector3 view;
            if (client == NetworkManager.ServerClientId)
            {
                if (!TryLocalView(out view))
                    return false;
            }
            else
            {
                if (!observerViews.TryGetValue(client, out var state) || !state.Valid || Time.realtimeSinceStartup - state.Received > 0.35f)
                    return false;
                view = state.Position;
            }
            for (int offset = 0; offset < 5; offset++)
            {
                Vector3 origin = view;
                if (client != NetworkManager.ServerClientId && offset > 0)
                    origin += (offset == 1 ? Vector3.right : offset == 2 ? Vector3.left : offset == 3 ? Vector3.forward : Vector3.back) * 3f;
                else if (offset > 0)
                    break;
                if (!Physics.Linecast(origin, point + Vector3.up * 0.25f, round.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) ||
                    !Physics.Linecast(origin, point + Vector3.up * 1.65f, round.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    return false;
            }
        }
        return true;
    }

    internal void Tick()
    {
        float now = Time.time;
        LogStartupState();
        if (!Network.IsHost)
        {
            TickReplica(now);
            return;
        }

        var round = StartOfRound.Instance;
        if (round == null)
        {
            EndRound();
            return;
        }
        if (round.shipIsLeaving || director != null && !round.shipHasLanded)
        {
            var shipTransform = round.elevatorTransform;
            if (!departureStarted && shipTransform != null)
            {
                departureStarted = true;
                foreach (var bot in bots)
                    bot.BeginShipDeparture(shipTransform);
            }
            foreach (var bot in bots)
            {
                if (shipTransform != null)
                    bot.FollowDepartingShip(shipTransform);
                if (bot.Available && bot.DepartingWithShip)
                    BotsSurvivedDeparture = true;
            }
            if (now >= nextPacket)
            {
                nextPacket = now + 0.2f;
                SendState();
            }
            return;
        }

        if (round.currentLevel != null && RoundStartPolicy.NeedsPreparation(roundPrepared, round.shipHasLanded,
            round.inShipPhase, round.shipIsLeaving, IsCompany(round.currentLevel)))
        {
            Plugin.Log.LogWarning("The level-generation callback was missed; preparing the bot crew from the landed ship state.");
            PrepareRound();
        }

        if (!round.inShipPhase && !round.shipHasLanded && now >= nextPreparationWarning)
        {
            nextPreparationWarning = now + 20f;
            Plugin.Log.LogInfo($"Waiting for ship landing before spawning bots (levelReady={roundPrepared}).");
        }

        if (!roundPrepared || !round.shipHasLanded || !round.localPlayerController || round.shipBounds == null || round.shipInnerRoomBounds == null ||
            round.currentLevel == null)
            return;
        if (IsCompany(round.currentLevel))
        {
            if (director != null || bots.Count > 0)
                EndRound();
            return;
        }

        if (now >= nextReconcile)
        {
            nextReconcile = now + 1f;
            if (!AllPeersReady())
            {
                if (now >= nextPeerWarning)
                {
                    nextPeerWarning = now + 20f;
                    Plugin.Log.LogWarning("Waiting for every connected player to use a compatible Lethal Fake Client version before spawning bots.");
                }
                ClearTeam();
                return;
            }
            if (director == null)
                director = new Director(round, learning!, HiddenFromHumanViews);
            if (needsFill)
            {
                needsFill = Fill(round);
            }
        }

        if (director == null)
            return;

        int index = bots.Count - 1;
        for (; index >= 0; --index)
        {
            var bot = bots[index];
            if (!bot.Available)
            {
                bool died = bot.Died;
                var player = bot.Body;
                if (died)
                {
                    retired.Add(player);
                    if (!bot.PhysicalBody.Dead)
                        Plugin.Log.LogInfo($"Bot {player.playerClientId} was reported dead by the game; cause and attacker are unavailable.");
                    bot.PhysicalBody.DropAll();
                    try
                    {
                        CreateBotBody(bot);
                    }
                    catch (Exception error)
                    {
                        Plugin.Log.LogError($"Could not synchronize death for bot {player.playerClientId}: {error}");
                    }
                }
                bot.Dispose();
                if (died && player != null)
                {
                    player.isPlayerDead = true;
                    RefreshDeadList();
                }
                bots.RemoveAt(index);
            }
        }

        if (hadBotsThisRound && bots.Count == 0 && round.livingPlayers <= 0 && !round.shipIsLeaving)
        {
            round.allPlayersDead = true;
            round.ShipLeaveAutomatically();
            return;
        }

        director.Validate(now);
        foreach (var bot in bots)
            bot.Tick(now);

        if (now >= nextPacket)
        {
            nextPacket = now + 0.2f;
            SendState();
        }
    }

    private void LogStartupState()
    {
        if (bots.Count > 0 || Time.realtimeSinceStartup < nextStartupDiagnostic)
            return;
        nextStartupDiagnostic = Time.realtimeSinceStartup + 5f;
        var round = StartOfRound.Instance;
        string state;
        if (round == null)
            state = $"host={Network.IsHost}, round=missing";
        else
        {
            string level = round.currentLevel != null ? round.currentLevel.sceneName : "missing";
            state = $"host={Network.IsHost}, level={level}, prepared={roundPrepared}, landed={round.shipHasLanded}, " +
                $"shipPhase={round.inShipPhase}, leaving={round.shipIsLeaving}, localPlayer={round.localPlayerController != null}, " +
                $"shipBounds={round.shipBounds != null}, shipRoom={round.shipInnerRoomBounds != null}, director={director != null}, needsFill={needsFill}";
        }
        if (state == lastStartupDiagnostic)
            return;
        lastStartupDiagnostic = state;
        Plugin.Log.LogInfo($"Bot startup state: {state}.");
    }

    internal void BotTeleported(PlayerControllerB player, Vector3 position)
    {
        if (!Network.IsHost)
            return;
        foreach (var bot in bots)
            if (bot.Body == player)
            {
                bot.Teleported(position);
                return;
            }
    }

    internal void HumanDroppedItem(GrabbableObject item)
    {
        if (!Network.IsHost || item == null)
            return;
        Blackboard.HumanDrop(item);
        director?.RequestReplan();
    }

    internal void DeliveryChanged() => director?.RequestReplan();

    private void CreateBotBody(CrewBot bot)
    {
        var body = bot.PhysicalBody;
        var player = bot.Body;
        if (player == null || player.playersManager == null)
            return;
        var round = player.playersManager;
        int animation = body.DeathAnimation;
        bool spawnBody = body.Dead && body.SpawnBody && round.playerRagdolls.Count > 0 && round.ragdollGrabbableObjectPrefab != null;
        if (spawnBody && (animation < 0 || animation >= round.playerRagdolls.Count || round.playerRagdolls[animation] == null))
            animation = 0;
        if (spawnBody && round.playerRagdolls[animation] == null)
            spawnBody = false;

        Vector3 position = player.transform.position;
        float yaw = player.transform.eulerAngles.y;
        if (spawnBody)
        {
            try
            {
                player.SpawnDeadBody((int)player.playerClientId, body.DeathVelocity, (int)body.DeathCause, player, animation);
                if (animation != 9)
                {
                    var itemObject = UnityEngine.Object.Instantiate(round.ragdollGrabbableObjectPrefab!, round.propsContainer);
                    var item = itemObject.GetComponent<RagdollGrabbableObject>();
                    var networkObject = itemObject.GetComponent<NetworkObject>();
                    if (item != null && networkObject != null)
                    {
                        item.isInFactory = bot.Inside;
                        networkObject.Spawn();
                        item.bodyID = (int)player.playerClientId;
                    }
                    else
                        UnityEngine.Object.Destroy(itemObject);
                }
            }
            catch (Exception error)
            {
                Plugin.Log.LogError($"Could not create body for bot {player.playerClientId}: {error}");
            }
        }

        recipients.Clear();
        foreach (ulong client in Network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && peers.Contains(client))
                recipients.Add(client);
        if (recipients.Count == 0)
            return;
        using var packet = new FastBufferWriter(sizeof(int) * 3 + sizeof(float) * 7 + sizeof(byte) + sizeof(ulong), Allocator.Temp);
        packet.WriteValueSafe((int)player.playerClientId);
        packet.WriteValueSafe(position);
        packet.WriteValueSafe(yaw);
        packet.WriteValueSafe(body.DeathVelocity);
        packet.WriteValueSafe((int)body.DeathCause);
        packet.WriteValueSafe(animation);
        packet.WriteValueSafe((byte)((bot.Aboard ? 1 : 0) | (spawnBody ? 2 : 0)));
        packet.WriteValueSafe(bot.AvatarSteamId);
        Network.CustomMessagingManager.SendNamedMessage(DeathMessage, recipients, packet, NetworkDelivery.ReliableSequenced);
    }

    private void ReceiveDeath(ulong sender, FastBufferReader packet)
    {
        var round = StartOfRound.Instance;
        if (sender != NetworkManager.ServerClientId || round == null ||
            !packet.TryBeginRead(sizeof(int) * 3 + sizeof(float) * 7 + sizeof(byte) + sizeof(ulong)))
            return;
        packet.ReadValueSafe(out int slot);
        packet.ReadValueSafe(out Vector3 position);
        packet.ReadValueSafe(out float yaw);
        packet.ReadValueSafe(out Vector3 velocity);
        packet.ReadValueSafe(out int cause);
        packet.ReadValueSafe(out int animation);
        packet.ReadValueSafe(out byte flags);
        packet.ReadValueSafe(out ulong avatarId);
        bool spawnBody = (flags & 2) != 0;
        if ((uint)slot >= (uint)round.allPlayerScripts.Length || round.ClientPlayerList.ContainsValue(slot))
            return;
        if (spawnBody && ((uint)animation >= (uint)round.playerRagdolls.Count || round.playerRagdolls[animation] == null))
            spawnBody = false;
        var player = round.allPlayerScripts[slot];
        if (player == null || retired.Contains(player))
            return;
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        player.isInElevator = (flags & 1) != 0;
        if (spawnBody)
        {
            try
            {
                player.SpawnDeadBody(slot, velocity, cause, player, animation);
            }
            catch (Exception error)
            {
                Plugin.Log.LogError($"Could not create remote body for bot {slot}: {error}");
            }
        }
        retired.Add(player);
        pendingEmotes.Remove(slot);
        BotAvatars.Assign(player, avatarId);
        if (replicas.TryGetValue(slot, out var replica))
        {
            player.isPlayerDead = true;
            replica.Body.Dispose();
            replicas.Remove(slot);
        }
        player.isPlayerControlled = false;
        player.isPlayerDead = true;
        player.DisablePlayerModel(player.gameObject);
        if (!replicas.ContainsKey(slot) && string.IsNullOrEmpty(player.playerUsername))
            player.playerUsername = $"Crew Bot {slot}";
        RefreshDeadList();
        if (round.livingPlayers <= 0 && replicas.Count == 0 && !round.shipIsLeaving)
        {
            round.allPlayersDead = true;
            round.ShipLeaveAutomatically();
        }
    }

    private static void RefreshDeadList()
    {
        var round = StartOfRound.Instance;
        var hud = HUDManager.Instance;
        if (round != null && round.localPlayerController != null && round.localPlayerController.isPlayerDead && hud != null)
            hud.UpdateBoxesSpectateUI();
    }

    internal void ReleaseRetired()
    {
        foreach (var player in retired)
            if (player != null && (StartOfRound.Instance == null || !StartOfRound.Instance.ClientPlayerList.ContainsValue((int)player.playerClientId)))
            {
                BotAvatars.Remove(player);
                player.isPlayerDead = false;
            }
        retired.Clear();
    }

    private bool AllPeersReady()
    {
        foreach (ulong client in Network.ConnectedClientsIds)
        {
            if (client != NetworkManager.ServerClientId && !peers.Contains(client))
                return false;
        }
        return true;
    }

    private static bool IsCompany(SelectableLevel level)
    {
        return LevelPolicy.IsCompany(level.sceneName, level.levelID, level.spawnEnemiesAndScrap);
    }

    private bool Fill(StartOfRound round)
    {
        var local = round.localPlayerController;
        bool retry = false;
        bool unoccupiedSlot = false;
        int count = 0;
        int slot = 0;
        for (; slot < round.allPlayerScripts.Length && bots.Count < BotSettings.BotLimit; ++slot)
        {
            var player = round.allPlayerScripts[slot];
            if (player != null && player != local && !retired.Contains(player) && !round.ClientPlayerList.ContainsValue(slot))
                unoccupiedSlot = true;
            if (player == null || player == local || player.isPlayerControlled || player.isPlayerDead || !player.IsOwnedByServer ||
                player.playerClientId != (ulong)slot || retired.Contains(player) || round.ClientPlayerList.ContainsValue(slot))
                continue;

            if (!director!.TryShipSpawn(slot, out var point))
            {
                if (Time.time >= nextSpawnWarning)
                {
                    nextSpawnWarning = Time.time + 20f;
                    Plugin.Log.LogWarning("No walkable spawn point was found inside the ship; retrying.");
                }
                retry = true;
                break;
            }
            var bot = CrewBot.Create(director, player, point, Mathf.Max(0.1f, local.movementSpeed));
            if (bot == null)
            {
                if (Time.time >= nextSpawnWarning)
                {
                    nextSpawnWarning = Time.time + 20f;
                    Plugin.Log.LogWarning("Could not attach bot navigation to a ship spawn point; retrying.");
                }
                retry = true;
                continue;
            }
            bots.Add(bot);
            hadBotsThisRound = true;
            SuitSelector.Assign(player, round);
            ++count;
        }

        if (count > 0)
        {
            SendState();
            Plugin.Log.LogInfo($"Director deployed {count} crew bots ({bots.Count} active).");
        }
        else if (bots.Count == 0 && BotSettings.BotLimit > 0 && !retry && Time.time >= nextSpawnWarning)
        {
            nextSpawnWarning = Time.time + 20f;
            var details = new System.Text.StringBuilder();
            for (int index = 0; index < round.allPlayerScripts.Length; index++)
            {
                var player = round.allPlayerScripts[index];
                if (player == null)
                {
                    details.Append($" slot={index}:missing;");
                    continue;
                }
                details.Append($" slot={index}:id={player.playerClientId},controlled={player.isPlayerControlled},dead={player.isPlayerDead},serverOwned={player.IsOwnedByServer},connected={round.ClientPlayerList.ContainsValue(index)},retired={retired.Contains(player)};");
            }
            Plugin.Log.LogWarning($"No eligible player slots for bots (limit={BotSettings.BotLimit}, players={round.allPlayerScripts.Length}).{details}");
        }
        return retry || count == 0 && bots.Count == 0 && BotSettings.BotLimit > 0 && unoccupiedSlot;
    }

    private void TickReplica(float now)
    {
        SendView(now);
        if (Network.IsConnectedClient && now >= nextPacket && now - lastPacket > 2f)
        {
            nextPacket = now + 2f;
            using var hello = new FastBufferWriter(1, Allocator.Temp);
            hello.WriteValueSafe(ProtocolVersion);
            Network.CustomMessagingManager.SendNamedMessage(HelloMessage, NetworkManager.ServerClientId, hello);
        }

        if (now - lastPacket > 3f || StartOfRound.Instance == null)
        {
            ClearReplicas();
            if (StartOfRound.Instance == null)
                ReleaseRetired();
            return;
        }

        float blend = 1f - Mathf.Exp(-14f * Time.deltaTime);
        foreach (var replica in replicas.Values)
        {
            var player = replica.Body.Player;
            if (player == null || !player.IsOwnedByServer || player == StartOfRound.Instance.localPlayerController)
                continue;
            replica.Body.SetJumping(replica.Jumping);
            replica.Body.SetEmote(replica.Emote);
            var round = StartOfRound.Instance;
            var ship = round.elevatorTransform;
            if (replica.ShipAnchored && ReplicaPolicy.ShouldReleaseAnchor(round.shipHasLanded, round.shipIsLeaving))
                replica.ShipAnchored = false;
            if (!replica.ShipAnchored && ship != null && ReplicaPolicy.ShouldAnchor(round.shipIsLeaving, (replica.Flags & 4) != 0))
            {
                replica.ShipAnchored = true;
                replica.ShipLocalPosition = ship.InverseTransformPoint(player.transform.position);
                replica.ShipLocalRotation = Quaternion.Inverse(ship.rotation) * player.transform.rotation;
                replica.ShipLocalLook = ship.InverseTransformPoint(replica.LookPoint);
            }
            if (replica.ShipAnchored && ship != null)
            {
                replica.Body.Pose(ship.TransformPoint(replica.ShipLocalPosition), ship.rotation * replica.ShipLocalRotation,
                    false, false, true);
                replica.Body.SetCrouching((replica.Flags & 16) != 0);
                replica.Body.SetTerminalPose((replica.Flags & 64) != 0);
                replica.Body.SetLeverPose((replica.Flags & 128) != 0);
                replica.Body.SetLook(ship.TransformPoint(replica.ShipLocalLook));
                continue;
            }
            Vector3 point = player.transform.position;
            Vector3 displacement = replica.Point - point;
            Vector3 nextPoint = displacement.sqrMagnitude > 64f ? replica.Point : Vector3.Lerp(point, replica.Point, blend);
            Vector3 velocity = displacement.sqrMagnitude > 64f ? Vector3.zero : (nextPoint - point) / Mathf.Max(Time.deltaTime, 0.001f);
            point = nextPoint;
            replica.Body.Pose(point, Quaternion.Slerp(player.transform.rotation, replica.Rotation, blend),
                (replica.Flags & 1) != 0, (replica.Flags & 2) != 0, (replica.Flags & 4) != 0, (replica.Flags & 8) != 0, velocity);
            replica.Body.SetCrouching((replica.Flags & 16) != 0);
            replica.Body.SetTerminalPose((replica.Flags & 64) != 0);
            replica.Body.SetLeverPose((replica.Flags & 128) != 0);
            if ((replica.Flags & 32) == 0)
                replica.Body.SetLook(replica.LookPoint);
        }
    }

    private void SendState()
    {
        recipients.Clear();
        foreach (ulong client in Network.ConnectedClientsIds)
        {
            if (client != NetworkManager.ServerClientId && peers.Contains(client))
                recipients.Add(client);
        }

        if (recipients.Count == 0)
            return;

        using var packet = new FastBufferWriter(sizeof(int) + bots.Count * RecordBytes, Allocator.Temp);
        packet.WriteValueSafe(bots.Count);
        foreach (var bot in bots)
        {
            packet.WriteValueSafe((int)bot.Body.playerClientId);
            packet.WriteValueSafe(bot.Position);
            packet.WriteValueSafe(bot.Body.transform.eulerAngles.y);
            packet.WriteValueSafe(bot.LookPoint);
            packet.WriteValueSafe((byte)((bot.Walking ? 1 : 0) | (bot.Inside ? 2 : 0) | (bot.Aboard ? 4 : 0) | (bot.Sprinting ? 8 : 0) | (bot.Crouching ? 16 : 0) | (bot.Frozen ? 32 : 0) | (bot.TerminalUsing ? 64 : 0) | (bot.LeverUsing ? 128 : 0)));
            packet.WriteValueSafe(ShipEmotePolicy.Pack(bot.Jumping, bot.ShipEmote));
            packet.WriteValueSafe(bot.AvatarSteamId);
        }

        Network.CustomMessagingManager.SendNamedMessage(StateMessage, recipients, packet, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void ReceiveState(ulong sender, FastBufferReader packet)
    {
        var round = StartOfRound.Instance;
        if (sender != NetworkManager.ServerClientId || round == null || !packet.TryBeginRead(sizeof(int)))
            return;

        packet.ReadValueSafe(out int count);
        if (count < 0 || count > MaxBots || !packet.TryBeginRead(count * RecordBytes))
            return;

        received.Clear();
        int index = 0;
        for (; index < count; ++index)
        {
            packet.ReadValueSafe(out int slot);
            packet.ReadValueSafe(out Vector3 point);
            packet.ReadValueSafe(out float yaw);
            packet.ReadValueSafe(out Vector3 lookPoint);
            packet.ReadValueSafe(out byte flags);
            packet.ReadValueSafe(out byte jumpFlag);
            packet.ReadValueSafe(out ulong avatarId);
            if ((uint)slot >= (uint)round.allPlayerScripts.Length)
                continue;

            var player = round.allPlayerScripts[slot];
            if (player == null || player == round.localPlayerController || !player.IsOwnedByServer || round.ClientPlayerList.ContainsValue(slot))
                continue;
            if (!received.Add(slot))
                continue;
            BotAvatars.Assign(player, avatarId);

            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            if (!replicas.TryGetValue(slot, out var replica))
            {
                if (!player.isPlayerDead && !retired.Contains(player))
                {
                    replica = new Replica(new BotBody(player), point, rotation, flags);
                    replica.LookPoint = lookPoint;
                    replica.Jumping = (jumpFlag & 1) != 0;
                    replica.Emote = ShipEmotePolicy.Emote(jumpFlag);
                    replicas.Add(slot, replica);
                    if (replica.Emote != 0 && pendingEmotes.TryGetValue(slot, out string custom))
                        replica.Body.SetCustomEmote(custom);
                    else if (replica.Emote == 0)
                        pendingEmotes.Remove(slot);
                }
                continue;
            }

            replica.Point = point;
            replica.Rotation = rotation;
            replica.LookPoint = lookPoint;
            replica.Flags = flags;
            replica.Jumping = (jumpFlag & 1) != 0;
            replica.Emote = ShipEmotePolicy.Emote(jumpFlag);
        }

        lastPacket = Time.time;
        expired.Clear();
        foreach (var pair in replicas)
        {
            if (!received.Contains(pair.Key))
                expired.Add(pair.Key);
        }

        foreach (int slot in expired)
        {
            pendingEmotes.Remove(slot);
            BotAvatars.Remove(replicas[slot].Body.Player);
            replicas[slot].Body.Dispose();
            replicas.Remove(slot);
        }
    }

    internal void ClearTeam()
    {
        if (!Network.IsHost)
            return;
        foreach (var bot in bots)
            bot.Dispose();
        if (bots.Count > 0)
        {
            bots.Clear();
            if (Network.IsListening && !Network.ShutdownInProgress)
                SendState();
        }
        needsFill = true;
    }

    private void EndRound()
    {
        if (!roundPrepared && director == null)
            return;
        Plugin.Log.LogInfo($"Bot round reset (prepared={roundPrepared}, director={director != null}).");
        var round = StartOfRound.Instance;
        if (round != null && round.shipIsLeaving)
            foreach (var bot in bots)
                if (bot.Available && bot.Aboard)
                {
                    BotsSurvivedDeparture = true;
                    break;
                }
        ClearTeam();
        DeliveryRegistry.Clear();
        director?.Dispose();
        director = null;
        roundPrepared = false;
        hadBotsThisRound = false;
        ReleaseRetired();
    }

    internal void BeforeRevive()
    {
        EndRound();
        if (!Network.IsHost)
        {
            foreach (var replica in replicas.Values)
                if (replica.Body.Player != null && replica.Body.Player.isPlayerDead)
                    retired.Add(replica.Body.Player);
            ClearReplicas();
            ReleaseRetired();
        }
    }

    private void ClearReplicas()
    {
        pendingEmotes.Clear();
        foreach (var replica in replicas.Values)
        {
            BotAvatars.Remove(replica.Body.Player);
            replica.Body.Dispose();
        }
        replicas.Clear();
    }

    public void Dispose()
    {
        if (Network.IsHost)
        {
            Network.OnClientDisconnectCallback -= Disconnected;
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(HelloMessage);
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(ViewMessage);
        }
        else
        {
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(StateMessage);
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(DeathMessage);
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(DropMessage);
            Network.CustomMessagingManager?.UnregisterNamedMessageHandler(EmoteMessage);
        }
        EndRound();
        ClearReplicas();
        ReleaseRetired();
    }
}
