using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace LethalFakeClient;

internal sealed class Director
{
    private static readonly AccessTools.FieldRef<Terminal, InteractTrigger> TerminalTrigger =
        AccessTools.FieldRefAccess<Terminal, InteractTrigger>("terminalTrigger");
    private static readonly AccessTools.FieldRef<ItemDropship, List<int>> DropshipOrders =
        AccessTools.FieldRefAccess<ItemDropship, List<int>>("itemsToDeliver");
    private sealed class ExitFailureState
    {
        internal readonly Dictionary<int, float> Failed = new Dictionary<int, float>();
        internal readonly Dictionary<int, int> RouteAttempts = new Dictionary<int, int>();
        internal float NoExitSince = -1f;
        internal float RescueUntil;
    }
    private const float ValidationInterval = 0.5f;
    private const float Commitment = 8f;
    private const float LateScrapRadius = 20f;
    private readonly List<IBot> bots = new List<IBot>();
    private readonly HashSet<GameNetcodeStuff.PlayerControllerB> crewParticipants = new HashSet<GameNetcodeStuff.PlayerControllerB>();
    private readonly HashSet<GameNetcodeStuff.PlayerControllerB> crewDeaths = new HashSet<GameNetcodeStuff.PlayerControllerB>();
    private readonly List<IBot> available = new List<IBot>();
    private readonly HashSet<IBot> assigned = new HashSet<IBot>();
    private readonly HashSet<GrabbableObject> reserved = new HashSet<GrabbableObject>();
    private readonly Dictionary<GrabbableObject, IBot> lootOwners = new Dictionary<GrabbableObject, IBot>();
    private readonly Dictionary<GrabbableObject, (IBot Receiver, float Until)> equipmentHandoffs = new Dictionary<GrabbableObject, (IBot, float)>();
    private readonly List<GrabbableObject> expiredHandoffs = new List<GrabbableObject>();
    private readonly List<IBot> explorationOwners = new List<IBot>();
    private readonly Dictionary<IBot, BotRole> roles = new Dictionary<IBot, BotRole>();
    private readonly List<IBot> roleCandidates = new List<IBot>();
    private readonly List<IBot> staleRoles = new List<IBot>();
    private readonly Dictionary<GrabbableObject, (IBot Bot, float Time)> staged = new Dictionary<GrabbableObject, (IBot Bot, float Time)>();
    private readonly Dictionary<GrabbableObject, (IBot Owner, float LastDog)> noisyLoot = new Dictionary<GrabbableObject, (IBot, float)>();
    private readonly List<GrabbableObject> noisyLootKeys = new List<GrabbableObject>();
    private readonly EntranceBatchPolicy entranceBatches = new EntranceBatchPolicy();
    private readonly Dictionary<int, int> entranceLootCounts = new Dictionary<int, int>();
    private readonly Dictionary<Vector3Int, float> visits = new Dictionary<Vector3Int, float>();
    private readonly Dictionary<GameNetcodeStuff.PlayerControllerB, Vector3> lastVisits = new Dictionary<GameNetcodeStuff.PlayerControllerB, Vector3>();
    private readonly List<EntranceTeleport> entrances = new List<EntranceTeleport>();
    private readonly List<NavMeshLinkInstance> jumpLinks = new List<NavMeshLinkInstance>();
    private readonly List<(Vector3 Start, Vector3 End)> jumpEndpoints = new List<(Vector3, Vector3)>();
    private readonly Dictionary<(Vector3Int, Vector3Int), float> jumpLinkAttempts = new Dictionary<(Vector3Int, Vector3Int), float>();
    private SteamValveHazard[] steamValves = System.Array.Empty<SteamValveHazard>();
    private float nextValveRefresh;
    private readonly List<TerminalAccessibleObject> terminalDevices = new List<TerminalAccessibleObject>();
    private readonly List<Vector3> outsideNodes = new List<Vector3>();
    private readonly List<Vector3> insideNodes = new List<Vector3>();
    private readonly Dictionary<DoorLock, AnimatedObjectTrigger> doors = new Dictionary<DoorLock, AnimatedObjectTrigger>();
    private readonly Dictionary<DoorLock, (NavMeshObstacle Obstacle, bool Carving)> doorObstacles = new Dictionary<DoorLock, (NavMeshObstacle, bool)>();
    private readonly Dictionary<DoorLock, float> recentlyClosed = new Dictionary<DoorLock, float>();
    private readonly Dictionary<(Vector2Int From, Vector2Int To), string> routeLearningKeys = new Dictionary<(Vector2Int, Vector2Int), string>();
    private readonly Dictionary<int, string> entranceLearningKeys = new Dictionary<int, string>();
    private readonly Dictionary<IBot, (EntranceTeleport? Portal, Vector3 Approach, float Checked)> escapePlans = new Dictionary<IBot, (EntranceTeleport?, Vector3, float)>();
    private readonly Dictionary<GameNetcodeStuff.PlayerControllerB, ExitFailureState> exitFailures = new Dictionary<GameNetcodeStuff.PlayerControllerB, ExitFailureState>();
    private readonly NavMeshPath planningPath = new NavMeshPath();
    private readonly Vector3[] planningCorners = new Vector3[64];
    private readonly Vector3[] landingNodes = new Vector3[6];
    private readonly float[] landingNodeDistances = new float[6];
    private readonly Dictionary<EntranceTeleport, (Vector3 Point, int Mask, float Until)> outdoorLandings = new Dictionary<EntranceTeleport, (Vector3, int, float)>();
    private readonly EntranceSafetyMemory entranceSafety = new EntranceSafetyMemory();
    private readonly Dictionary<GrabbableObject, float> storedEquipment = new Dictionary<GrabbableObject, float>();
    private readonly Learning learning;
    private readonly string learningMoon;
    private readonly BotHistory history = new BotHistory();
    private ItemCharger? charger;
    private ShipTeleporter? teleporter;
    private Terminal? shipTerminal;
    private HangarShipDoor? hangarDoor;
    private ItemDropship? dropship;
    private InteractTrigger? lightsSwitch;
    private float nextLightsSearch;
    private float nextLightsAction;
    private readonly Dictionary<GameNetcodeStuff.PlayerControllerB, float> recentTeleports = new Dictionary<GameNetcodeStuff.PlayerControllerB, float>();
    private IBot? doorOperator;
    private StartMatchLever? departureLever;
    private CrewBot? departureBot;
    private float departureAnimationUntil;
    private bool automaticDoorClosed;
    private float lastDoorThreat = float.NegativeInfinity;
    private float nextDoorAction;
    private float shipControlUntil;
    private GameNetcodeStuff.PlayerControllerB? rescueTarget;
    private float rescueDeadline;
    private readonly StartOfRound round;
    private readonly System.Func<Vector3, bool> hiddenFromHumanViews;
    private readonly Dictionary<IBot, float> hiddenEntryRetry = new Dictionary<IBot, float>();
    private readonly float directorStartedAt;
    private float nextHiddenEntryCheck;
    private readonly Vector3 ship;
    private readonly NavMeshDataInstance shipMesh;
    private readonly NavMeshData? shipMeshData;
    private List<GrabbableObject> scrap = null!;
    private float nextValidation;
    private float nextSave;
    private float nextEntranceRefresh;
    private bool dirty = true;
    private bool facilityCrewPresent;
    private IBot? guardian;
    private IBot? elevatorOwner;
    private readonly Dictionary<IBot, int> elevatorSeats = new Dictionary<IBot, int>();
    private float elevatorLease;
    private float nextElevatorAction;
    private float nextShipSupport;
    private float nextRadarSwitch;
    private float nextQuotaCheck;
    private float quotaGap;
    private float quotaUrgency;

    internal Blackboard Board { get; }
    internal ItemCharger? Charger => charger;
    internal void StoreEquipment(GrabbableObject item, float now) => storedEquipment[item] = now + 90f;
    internal bool HasShipAmmo(int ammoType)
    {
        foreach (var item in Blackboard.Items)
            if (item is GunAmmo ammo && ammo.ammoType == ammoType && item.IsSpawned && item.isInShipRoom &&
                !item.isHeld && !item.heldByPlayerOnServer && !item.isHeldByEnemy)
                return true;
        return false;
    }

    private bool CanEquip(GrabbableObject item, float now)
    {
        if (!storedEquipment.TryGetValue(item, out float until))
            return true;
        if (now < until)
            return false;
        storedEquipment.Remove(item);
        return true;
    }

    private float TimePressure()
    {
        var clock = TimeOfDay.Instance;
        if (clock == null || clock.numberOfHours <= 0 || round.currentLevel == null || !round.currentLevel.planetHasTime)
            return 0f;
        float normalized = clock.totalTime > 0f ? clock.currentDayTime / clock.totalTime : clock.normalizedTimeOfDay;
        return Mathf.InverseLerp(12f, 16f, normalized * clock.numberOfHours);
    }

    private void RefreshQuota(float now)
    {
        if (now < nextQuotaCheck)
            return;
        nextQuotaCheck = now + 5f;
        var clock = TimeOfDay.Instance;
        if (clock == null || clock.profitQuota <= 0)
        {
            quotaGap = 0f;
            quotaUrgency = 0f;
            return;
        }
        int shipValue = 0;
        foreach (var item in Blackboard.Items)
            if (item != null && item.IsSpawned && item.itemProperties != null && item.itemProperties.isScrap &&
                !item.deactivated && !item.itemUsedUp && (item.isInShipRoom || round.shipInnerRoomBounds.bounds.Contains(item.transform.position)))
                shipValue += Mathf.Max(0, item.scrapValue);
        float buyingRate = round.companyBuyingRate > 0f ? Mathf.Clamp(round.companyBuyingRate, 0.25f, 1f) : 1f;
        quotaGap = Mathf.Max(0f, (clock.profitQuota - clock.quotaFulfilled) / buyingRate - shipValue);
        quotaUrgency = quotaGap > 0f ? Mathf.Clamp01(0.4f + (2 - clock.daysUntilDeadline) * 0.2f) : 0f;
    }

    private float SecondsUntilDeparture()
    {
        var clock = TimeOfDay.Instance;
        if (clock == null || round.currentLevel == null || !round.currentLevel.planetHasTime ||
            !clock.currentDayTimeStarted || clock.totalTime <= 0f)
            return float.PositiveInfinity;
        float rate = clock.globalTimeSpeedMultiplier * round.currentLevel.DaySpeedMultiplier;
        if (rate <= 0f)
            return float.PositiveInfinity;
        return Mathf.Max(0f, (clock.totalTime * clock.shipLeaveAutomaticallyTime - clock.currentDayTime) / rate);
    }

    private bool CollectionFits(IBot bot, GrabbableObject item)
    {
        float remaining = SecondsUntilDeparture();
        if (remaining > 180f)
            return true;
        int carriedValue = 0;
        foreach (var held in bot.Body.ItemSlots)
            if (held != null && held.itemProperties != null && held.itemProperties.isScrap)
                carriedValue += Mathf.Max(0, held.scrapValue);
        Vector3 itemPosition = item.transform.position;
        float trip = EstimatedDistance(bot.Position, itemPosition) + EstimatedDistance(itemPosition, ship);
        return ExtractionPolicy.CanCollect(remaining, trip, bot.Body.carryWeight, carriedValue, item.scrapValue);
    }

    private float EstimatedDistance(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out var start, 2f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out var end, 2f, NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, planningPath) ||
            planningPath.status != NavMeshPathStatus.PathComplete)
            return Vector3.Distance(from, to) * 1.8f;
        int count = planningPath.GetCornersNonAlloc(planningCorners);
        if (count == 0 || count == planningCorners.Length)
            return Vector3.Distance(from, to) * 1.8f;
        float length = Vector3.Distance(from, start.position) + Vector3.Distance(end.position, to);
        for (int index = 1; index < count; index++)
            length += Vector3.Distance(planningCorners[index - 1], planningCorners[index]);
        return length;
    }

    private bool MustReturnNow(IBot bot, float secondsLeft, float now)
    {
        if (bot.Aboard || secondsLeft > 180f)
            return false;
        float distance;
        if (bot.Inside)
        {
            var exit = PlannedEscape(bot, now, out var approach);
            if (exit == null || exit.exitScript == null || exit.exitScript.entrancePoint == null)
                return secondsLeft < 45f;
            distance = EstimatedDistance(bot.Position, approach) +
                EstimatedDistance(exit.exitScript.entrancePoint.position, ship) + 12f;
        }
        else
            distance = EstimatedDistance(bot.Position, ship);
        return !ExtractionPolicy.CanCollect(secondsLeft, distance, bot.Body.carryWeight, 0, 0);
    }

    internal void Record(IBot bot, string name, string details) => history.Record(bot, name, details);

    private string EntranceKey(int id)
    {
        if (!entranceLearningKeys.TryGetValue(id, out string key))
        {
            key = $"moon:{learningMoon}:entrance:{id}";
            entranceLearningKeys.Add(id, key);
        }
        return key;
    }

    private string? RouteKey(Vector3 from, Vector3 to)
    {
        var origin = new Vector2Int(Mathf.FloorToInt(from.x / 32f), Mathf.FloorToInt(from.z / 32f));
        var destination = new Vector2Int(Mathf.FloorToInt(to.x / 32f), Mathf.FloorToInt(to.z / 32f));
        if (origin == destination)
            return null;
        var pair = (origin, destination);
        if (routeLearningKeys.TryGetValue(pair, out string key))
            return key;
        key = $"moon:{learningMoon}:route:{origin.x},{origin.y}:{destination.x},{destination.y}";
        if (routeLearningKeys.Count < 256)
            routeLearningKeys.Add(pair, key);
        return key;
    }

    internal void ObserveOutdoorRoute(Vector3 from, Vector3 to, float outcome)
    {
        string? key = RouteKey(from, to);
        if (key != null)
            learning.Observe(key, outcome);
    }

    internal void ObserveEntrance(int id, float outcome) => learning.Observe(EntranceKey(id), outcome);

    private float OutdoorRouteEstimate(Vector3 from, Vector3 to)
    {
        string? key = RouteKey(from, to);
        return key != null ? learning.Estimate(key) : 0f;
    }
    internal Vector3 Ship => ship;
    internal ItemDropship? Dropship => dropship;
    internal IReadOnlyList<Vector3> IndoorNodes => insideNodes;
    internal IReadOnlyList<Vector3> OutdoorNodes => outsideNodes;
    internal bool ShipOpen => !round.hangarDoorsClosed;
    internal bool EarlyDepartureVote => DirectorClock.EarlyVoteReturn(round.shipHasLanded, round.shipIsLeaving,
        round.inShipPhase, TimeOfDay.Instance != null ? TimeOfDay.Instance.votesForShipToLeaveEarly : 0);

    internal IBot? NearbyLookingCompanion(IBot bot)
    {
        IBot? closest = null;
        float best = 25f;
        foreach (var other in bots)
        {
            if (other == bot || !other.Available || other.Inside != bot.Inside || other.Aboard != bot.Aboard)
                continue;
            float distance = (other.Position - bot.Position).sqrMagnitude;
            if (distance >= best)
                continue;
            closest = other;
            best = distance;
        }
        return closest;
    }
    internal MineshaftElevatorController? Elevator { get; private set; }
    internal BridgeTraffic Bridges { get; } = new BridgeTraffic();

    internal bool TryJumpLink(NavMeshAgent agent, Vector3 start, Vector3 end, bool inside, float now)
    {
        Vector3 delta = end - start;
        float height = delta.y;
        delta.y = 0f;
        if (!MovementPolicy.CanJumpGap(delta.sqrMagnitude, height) || jumpLinks.Count >= 64)
            return false;
        var key = (Vector3Int.FloorToInt(start), Vector3Int.FloorToInt(end));
        if (jumpLinkAttempts.TryGetValue(key, out float retry) && now < retry)
            return false;
        if (jumpLinkAttempts.Count >= 512)
            jumpLinkAttempts.Clear();
        jumpLinkAttempts[key] = now + 10f;
        Vector3 direction = delta.normalized;
        if (!NavMesh.SamplePosition(start - direction * 0.35f, out var takeoff, 0.6f, agent.areaMask) ||
            !NavMesh.SamplePosition(end + direction * 0.35f, out var landing, 0.6f, agent.areaMask))
            return false;
        Vector3 actualGap = landing.position - takeoff.position;
        float actualHeight = actualGap.y;
        actualGap.y = 0f;
        if (!MovementPolicy.CanJumpGap(actualGap.sqrMagnitude, actualHeight) ||
            !JumpPathSafe(takeoff.position, landing.position, inside))
            return false;

        var link = NavMesh.AddLink(new NavMeshLinkData
        {
            startPosition = takeoff.position,
            endPosition = landing.position,
            bidirectional = true,
            width = 0.5f,
            costModifier = 2f,
            area = 0,
            agentTypeID = agent.agentTypeID
        });
        if (!link.valid)
            return false;
        if (!agent.CalculatePath(landing.position, planningPath) || planningPath.status != NavMeshPathStatus.PathComplete)
        {
            link.Remove();
            return false;
        }
        jumpLinks.Add(link);
        jumpEndpoints.Add((takeoff.position, landing.position));
        Plugin.Log.LogInfo($"Created {(inside ? "indoor" : "outdoor")} jump route from {takeoff.position} to {landing.position}.");
        return true;
    }

    internal bool JumpPathSafe(Vector3 start, Vector3 end, bool inside)
    {
        if (inside && (JumpCrossesDoor(start, end) || Board.Spikes.Crossing(start, end, true) != null))
            return false;
        Vector3 previous = start + Vector3.up * 0.65f;
        for (int index = 0; index <= 8; index++)
        {
            Vector3 point = Vector3.Lerp(start, end, index / 8f);
            if (Board.AreaRisk(point, inside) >= 0.35f || Board.StaticEnvironmentalRisk(point, inside) >= 0.35f)
                return false;
        }
        for (int index = 1; index <= 8; index++)
        {
            float t = index / 8f;
            Vector3 next = Vector3.Lerp(start, end, t) +
                Vector3.up * (0.65f + 1.5f * 4f * t * (1f - t));
            Vector3 segment = next - previous;
            if (Physics.SphereCast(previous, 0.3f, segment.normalized, out _, segment.magnitude,
                Board.SightMask, QueryTriggerInteraction.Ignore) ||
                Physics.SphereCast(previous + Vector3.up * 0.8f, 0.3f, segment.normalized, out _, segment.magnitude,
                    Board.SightMask, QueryTriggerInteraction.Ignore))
                return false;
            previous = next;
        }
        return true;
    }

    private bool JumpCrossesDoor(Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        segment.y = 0f;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.01f)
            return false;
        foreach (var door in doors.Keys)
        {
            if (door == null || !door.gameObject.activeInHierarchy)
                continue;
            Vector3 offset = door.transform.position - start;
            float height = offset.y;
            offset.y = 0f;
            float fraction = Mathf.Clamp01(Vector3.Dot(offset, segment) / lengthSquared);
            if (MovementPolicy.DoorIntersectsJump((offset - segment * fraction).sqrMagnitude,
                height - (end.y - start.y) * fraction))
                return true;
        }
        return false;
    }

    internal bool IsJumpLink(Vector3 start, Vector3 end)
    {
        foreach (var link in jumpEndpoints)
            if ((link.Start - start).sqrMagnitude < 1f && (link.End - end).sqrMagnitude < 1f ||
                (link.Start - end).sqrMagnitude < 1f && (link.End - start).sqrMagnitude < 1f)
                return true;
        return false;
    }

    internal SteamValveHazard[] SteamValves(float now)
    {
        if (now >= nextValveRefresh)
        {
            steamValves = UnityEngine.Object.FindObjectsOfType<SteamValveHazard>();
            nextValveRefresh = now + 10f;
        }
        return steamValves;
    }

    internal Director(StartOfRound round, Learning learning, System.Func<Vector3, bool> hiddenFromHumanViews)
    {
        this.round = round;
        this.hiddenFromHumanViews = hiddenFromHumanViews;
        directorStartedAt = Time.time;
        for (int slot = 0; slot < round.allPlayerScripts.Length; slot++)
        {
            var player = round.allPlayerScripts[slot];
            if (player == null || player.disconnectedMidGame ||
                !player.isPlayerControlled && !(player.isPlayerDead && round.ClientPlayerList.ContainsValue(slot)))
                continue;
            crewParticipants.Add(player);
            if (player.isPlayerDead)
                crewDeaths.Add(player);
        }
        charger = UnityEngine.Object.FindObjectOfType<ItemCharger>();
        FindTeleporter();
        shipTerminal = UnityEngine.Object.FindObjectOfType<Terminal>();
        hangarDoor = UnityEngine.Object.FindObjectOfType<HangarShipDoor>();
        dropship = UnityEngine.Object.FindObjectOfType<ItemDropship>();
        departureLever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
        Elevator = UnityEngine.Object.FindObjectOfType<MineshaftElevatorController>();
        this.learning = learning;
        learningMoon = (round.currentLevel.sceneName ?? "unknown") + ":" + round.currentLevel.levelID;
        Board = new Blackboard(round.collidersAndRoomMaskAndDefault);
        ship = FindShipPoint(round);
        shipMesh = AddShipNavigation(round, ship, out shipMeshData);
        foreach (var entrance in UnityEngine.Object.FindObjectsOfType<EntranceTeleport>())
        {
            if (entrance.entrancePoint != null)
                entrances.Add(entrance);
        }
        terminalDevices.AddRange(UnityEngine.Object.FindObjectsOfType<TerminalAccessibleObject>());
        foreach (var door in UnityEngine.Object.FindObjectsOfType<DoorLock>())
        {
            if (door.TryGetComponent<AnimatedObjectTrigger>(out var trigger))
                doors.Add(door, trigger);
            if (door.TryGetComponent<NavMeshObstacle>(out var obstacle))
                doorObstacles.Add(door, (obstacle, obstacle.carving));
        }

        ReadNodes("OutsideAINode", outsideNodes);
        ReadNodes("AINode", insideNodes);
    }

    private static void ReadNodes(string tag, List<Vector3> nodes)
    {
        foreach (var node in GameObject.FindGameObjectsWithTag(tag))
            nodes.Add(node.transform.position);
    }

    private static Vector3 FindShipPoint(StartOfRound round)
    {
        Vector3 center = round.shipInnerRoomBounds.bounds.center;
        if (Physics.Raycast(center, Vector3.down, out var floor, 5f, round.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            return floor.point + Vector3.up * 0.05f;
        return center;
    }

    private static NavMeshDataInstance AddShipNavigation(StartOfRound round, Vector3 ship, out NavMeshData? data)
    {
        data = null;
        if (NavMesh.SamplePosition(ship, out var existing, 0.7f, NavMesh.AllAreas) &&
            IsInsideShip(round.shipInnerRoomBounds, existing.position))
            return default;

        var bounds = round.shipBounds.bounds;
        bounds.Expand(new Vector3(4f, 2f, 4f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, round.collidersAndRoomMaskAndDefault, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(source => source.component is Collider collider && collider.isTrigger);
        if (sources.Count == 0 || NavMesh.GetSettingsCount() == 0)
            return default;

        data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources, bounds, Vector3.zero, Quaternion.identity);
        return data != null ? NavMesh.AddNavMeshData(data) : default;
    }

    private static bool IsInsideShip(Collider room, Vector3 floorPoint)
    {
        if (room == null)
            return false;
        Vector3 bodyPoint = floorPoint + Vector3.up * 0.45f;
        return room.bounds.Contains(bodyPoint) && (room.ClosestPoint(bodyPoint) - bodyPoint).sqrMagnitude < 0.01f;
    }

    internal bool TryShipSpawn(int slot, out Vector3 point)
    {
        point = default;
        var room = round.shipInnerRoomBounds;
        if (room == null)
            return false;
        var positions = round.playerSpawnPositions;
        int count = positions != null ? positions.Length : 0;
        for (int index = 0; index < count + 1; index++)
        {
            Transform? spawn = count > 0 && index < count ? positions![(slot + index) % count] : null;
            Vector3 origin = spawn != null ? spawn.position : ship;
            for (int attempt = 0; attempt < 9; attempt++)
            {
                float angle = (attempt - 1) * Mathf.PI * 0.25f;
                float radius = attempt == 0 ? 0f : 0.65f;
                Vector3 candidate = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!NavMesh.SamplePosition(candidate, out var hit, 0.9f, NavMesh.AllAreas) ||
                    Mathf.Abs(hit.position.y - origin.y) > 0.9f || !IsInsideShip(room, hit.position))
                    continue;
                point = hit.position;
                return true;
            }
        }
        return false;
    }

    internal bool OpenNearbyDoor(IBot bot, Vector3 goal, float now, bool fleeing = false)
    {
        if (!bot.Inside)
            return false;

        foreach (var pair in doors)
        {
            var door = pair.Key;
            if (door == null || pair.Value == null || pair.Value.boolValue || !pair.Value.isBool)
                continue;
            if (recentlyClosed.TryGetValue(door, out float closedUntil) && now < closedUntil)
                continue;
            Vector3 point = door.transform.position;
            Vector3 toDoor = point - bot.Position;
            Vector3 toGoal = goal - bot.Position;
            toDoor.y = 0f;
            toGoal.y = 0f;
            if (Mathf.Abs(point.y - bot.Position.y) > 2.5f || toDoor.sqrMagnitude > 6.25f ||
                Vector3.Dot(toDoor, toGoal) <= 0.35f * Mathf.Sqrt(toDoor.sqrMagnitude * toGoal.sqrMagnitude) ||
                !fleeing && Board.Risk(point, true, now) > 0.35f)
                continue;

            if (door.isLocked)
            {
                if (!BotBody.TryGet(bot.Body, out var body) || !body.UseKey(door))
                    continue;
            }

            door.OpenOrCloseDoor(bot.Body);
            if (pair.Value.boolValue)
                Board.RouteRecovered(goal);
            return pair.Value.boolValue;
        }

        return false;
    }

    internal void CloseDoorBehind(IBot bot, Vector3 threat, float now)
    {
        if (!bot.Inside)
            return;

        foreach (var pair in doors)
        {
            var door = pair.Key;
            if (door == null || pair.Value == null || !pair.Value.isBool || !pair.Value.boolValue || door.isLocked)
                continue;
            Vector3 doorPosition = door.transform.position;
            Vector3 escapeDirection = doorPosition - threat;
            escapeDirection.y = 0f;
            Vector3 passedDoor = bot.Position - doorPosition;
            passedDoor.y = 0f;
            if (Mathf.Abs(doorPosition.y - bot.Position.y) > 2.5f || passedDoor.sqrMagnitude < 0.64f || passedDoor.sqrMagnitude > 16f ||
                escapeDirection.sqrMagnitude < 1f || escapeDirection.sqrMagnitude > 400f || Vector3.Dot(passedDoor, escapeDirection) <= 0f)
                continue;

            bool teammateBehind = false;
            foreach (var player in round.allPlayerScripts)
            {
                if (player == null || player == bot.Body || !player.isPlayerControlled || player.isPlayerDead || !player.isInsideFactory)
                    continue;
                Vector3 offset = player.transform.position - doorPosition;
                if (Mathf.Abs(offset.y) > 2.5f || offset.sqrMagnitude > 900f)
                    continue;
                offset.y = 0f;
                if (offset.sqrMagnitude < 4f || Vector3.Dot(offset, escapeDirection) <= 0f)
                {
                    teammateBehind = true;
                    break;
                }
            }
            if (teammateBehind)
                continue;

            door.OpenOrCloseDoor(bot.Body);
            if (!pair.Value.boolValue)
                recentlyClosed[door] = now + 4f;
            return;
        }
    }

    internal DoorLock? LockedDoorNear(Vector3 point)
    {
        DoorLock? nearest = null;
        float nearestDistance = 9f;
        foreach (var door in doors.Keys)
        {
            if (door == null || !door.isLocked || door.isPickingLock)
                continue;
            float distance = (door.transform.position - point).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = door;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    internal bool TryOutsideLanding(EntranceTeleport outside, int areaMask, float now, out Vector3 landing)
    {
        landing = default;
        if (outside == null || outside.entrancePoint == null)
            return false;
        if (outdoorLandings.TryGetValue(outside, out var cached) && cached.Mask == areaMask && now < cached.Until &&
            Board.Risk(cached.Point, false, now, false) < 0.48f && LandingClear(cached.Point))
        {
            landing = cached.Point;
            return true;
        }
        Vector3 exit = outside.entrancePoint.position;
        Vector3 forward = outside.entrancePoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        else
            forward.Normalize();
        int nodeCount = 0;
        foreach (var node in outsideNodes)
        {
            float distance = (node - exit).sqrMagnitude;
            if (distance > 10000f || Mathf.Abs(node.y - exit.y) > 30f ||
                !NavMesh.SamplePosition(node, out var nodeHit, 3f, areaMask) || Mathf.Abs(nodeHit.position.y - node.y) > 3f)
                continue;
            int index = Mathf.Min(nodeCount, landingNodes.Length - 1);
            if (nodeCount == landingNodes.Length && distance >= landingNodeDistances[index])
                continue;
            while (index > 0 && distance < landingNodeDistances[index - 1])
            {
                landingNodes[index] = landingNodes[index - 1];
                landingNodeDistances[index] = landingNodeDistances[index - 1];
                index--;
            }
            landingNodes[index] = nodeHit.position;
            landingNodeDistances[index] = distance;
            nodeCount = Mathf.Min(nodeCount + 1, landingNodes.Length);
        }

        float best = float.PositiveInfinity;
        for (int attempt = 0; attempt < 41; attempt++)
        {
            float radius = attempt == 0 ? 0f : 1f + (attempt - 1) / 8;
            float angle = (attempt - 1) * 45f;
            Vector3 desired = exit + Quaternion.Euler(0f, angle, 0f) * forward * radius;
            if (!NavMesh.SamplePosition(desired, out var hit, 3f, areaMask) || Mathf.Abs(hit.position.y - exit.y) > 3f)
                continue;
            Vector3 offset = hit.position - desired;
            offset.y = 0f;
            if (offset.sqrMagnitude > 0.64f || Board.Risk(hit.position, false, now, false) >= 0.48f || !LandingClear(hit.position))
                continue;
            if (nodeCount == 0)
            {
                if (attempt == 0)
                {
                    landing = hit.position;
                    return true;
                }
                continue;
            }
            for (int index = 0; index < nodeCount; index++)
            {
                if (!NavMesh.CalculatePath(hit.position, landingNodes[index], areaMask, planningPath) ||
                    planningPath.status != NavMeshPathStatus.PathComplete)
                    continue;
                int count = planningPath.GetCornersNonAlloc(planningCorners);
                if (count == 0 || count == planningCorners.Length ||
                    count == 1 && (hit.position - landingNodes[index]).sqrMagnitude > 0.25f ||
                    count > 1 && !ClearLandingPath(count))
                    continue;
                if (attempt == 0)
                {
                    landing = hit.position;
                    outdoorLandings[outside] = (landing, areaMask, now + 5f);
                    return true;
                }
                float score = (hit.position - exit).sqrMagnitude + Mathf.Sqrt(landingNodeDistances[index]) * 0.03f;
                if (score < best)
                {
                    best = score;
                    landing = hit.position;
                }
                break;
            }
        }
        if (float.IsPositiveInfinity(best))
            return false;
        outdoorLandings[outside] = (landing, areaMask, now + 5f);
        return true;
    }

    private bool LandingClear(Vector3 point) =>
        !Physics.CheckCapsule(point + Vector3.up * 0.4f, point + Vector3.up * 1.4f,
            0.22f, Board.SightMask, QueryTriggerInteraction.Ignore);

    private bool ClearLandingPath(int count)
    {
        float remaining = 6f;
        for (int index = 1; index < count && remaining > 0f; index++)
        {
            Vector3 segment = planningCorners[index] - planningCorners[index - 1];
            float distance = segment.magnitude;
            if (distance < 0.01f)
                continue;
            float checkedDistance = Mathf.Min(distance, remaining);
            Vector3 start = planningCorners[index - 1] + Vector3.up;
            if (Physics.Linecast(start, start + segment * (checkedDistance / distance), Board.SightMask, QueryTriggerInteraction.Ignore))
                return false;
            remaining -= checkedDistance;
        }
        return true;
    }

    internal EntranceTeleport? Portal(IBot bot, float now, out Vector3 approach, Vector3? preferredDestination = null, Vector3? fleeFrom = null)
    {
        EntranceTeleport? best = null;
        float bestScore = float.PositiveInfinity;
        approach = default;
        foreach (var source in entrances)
        {
            if (source == null || source.isEntranceToBuilding == bot.Inside || source.entrancePoint == null)
                continue;
            if (bot.Inside && FailedExit(bot.Body, source.entranceId, now))
                continue;
            if (source.exitScript == null)
            {
                foreach (var exit in entrances)
                {
                    if (exit != null && exit.entranceId == source.entranceId && exit.isEntranceToBuilding != source.isEntranceToBuilding)
                    {
                        source.exitScript = exit;
                        break;
                    }
                }
            }
            if (source.exitScript == null || source.exitScript.entrancePoint == null)
                continue;

            Vector3 entry = source.entrancePoint.position;
            Vector3 destination = source.exitScript.entrancePoint.position;
            if (!bot.Inside && !EntranceAllowed(source, now))
                continue;
            bool nearbyEntrance = !bot.Inside && IsNearbyEntrance(bot.Position, entry);
            if (!bot.CanAttempt(entry, bot.Inside, now) || !bot.TryPortalApproach(entry, out var candidate))
                continue;
            bool includeRouteFailures = bot.Cargo == null && bot.Order.Kind != TaskKind.Return &&
                bot.Order.Kind != TaskKind.Guard && !IsLate();
            float entryRisk = Board.Risk(entry, bot.Inside, now, nearbyEntrance ? false : includeRouteFailures);
            float exitRisk = Board.Risk(destination, !bot.Inside, now, nearbyEntrance ? false : includeRouteFailures);
            if (fleeFrom.HasValue)
            {
                if (exitRisk >= 0.48f || Board.EnvironmentalRisk(entry, bot.Inside) >= 0.55f)
                    continue;
            }
            else if (Mathf.Max(entryRisk, exitRisk) >= 0.35f)
                continue;
            float risk = fleeFrom.HasValue ? exitRisk : Mathf.Max(entryRisk, exitRisk);
            if (nearbyEntrance && IsPortalApproach(candidate, entry))
            {
                approach = candidate;
                return source;
            }
            float score = Vector3.Distance(bot.Position, candidate) + 120f * risk;
            score -= learning.Estimate(EntranceKey(source.entranceId)) * 10f;
            if (!bot.Inside)
                score -= OutdoorRouteEstimate(bot.Position, candidate) * 8f;
            if (fleeFrom.HasValue)
            {
                float distanceFromThreat = Vector3.Distance(entry, fleeFrom.Value);
                if (distanceFromThreat < 7f)
                    score += (7f - distanceFromThreat) * 20f;
            }
            if (preferredDestination.HasValue)
                score += Vector3.Distance(destination, preferredDestination.Value) * 3f;
            if (score < bestScore)
            {
                bestScore = score;
                best = source;
                approach = candidate;
            }
        }
        return best;
    }

    internal void PlanEscape(IBot bot, float now, bool force = false)
    {
        if (!bot.Available || !bot.Inside)
        {
            escapePlans.Remove(bot);
            return;
        }
        if (!force && escapePlans.TryGetValue(bot, out var previous) && now - previous.Checked < 5f)
            return;

        EntranceTeleport? best = null;
        Vector3 bestApproach = default;
        float bestScore = float.PositiveInfinity;
        foreach (var entrance in entrances)
        {
            if (entrance == null || entrance.isEntranceToBuilding || entrance.entrancePoint == null)
                continue;
            if (FailedExit(bot.Body, entrance.entranceId, now))
                continue;
            if (entrance.exitScript == null)
                foreach (var exit in entrances)
                    if (exit != null && exit.isEntranceToBuilding && exit.entranceId == entrance.entranceId)
                    {
                        entrance.exitScript = exit;
                        break;
                    }
            if (entrance.exitScript == null || entrance.exitScript.entrancePoint == null ||
                Board.Risk(entrance.exitScript.entrancePoint.position, false, now, false) >= 0.48f ||
                !bot.CanAttempt(entrance.entrancePoint.position, true, now) ||
                !bot.TryPortalApproach(entrance.entrancePoint.position, out var approach) ||
                (approach - entrance.entrancePoint.position).sqrMagnitude > 12.25f ||
                !SafePlanningPath(bot.Position, approach, now, out float length))
                continue;
            float risk = Board.Risk(approach, true, now, false);
            float score = length + risk * 60f - learning.Estimate(EntranceKey(entrance.entranceId)) * 12f;
            if (score >= bestScore)
                continue;
            best = entrance;
            bestApproach = approach;
            bestScore = score;
        }
        escapePlans[bot] = (best, bestApproach, now);
    }

    private bool SafePlanningPath(Vector3 from, Vector3 to, float now, out float length)
    {
        length = 0f;
        if (!NavMesh.SamplePosition(from, out var start, 2f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out var end, 2f, NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, planningPath) ||
            planningPath.status != NavMeshPathStatus.PathComplete)
            return false;
        int count = planningPath.GetCornersNonAlloc(planningCorners);
        if (count == 0 || count == planningCorners.Length)
            return false;
        for (int index = 1; index < count; index++)
        {
            Vector3 segment = planningCorners[index] - planningCorners[index - 1];
            float distance = segment.magnitude;
            length += distance;
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
            for (int sample = 1; sample <= samples; sample++)
                if (Board.Risk(planningCorners[index - 1] + segment * (sample / (float)samples), true, now, false) >= 0.55f)
                    return false;
        }
        return true;
    }

    internal EntranceTeleport? PlannedEscape(IBot bot, float now, out Vector3 approach)
    {
        PlanEscape(bot, now);
        if (escapePlans.TryGetValue(bot, out var plan) && plan.Portal != null && plan.Portal.isActiveAndEnabled &&
            plan.Portal.entrancePoint != null && bot.CanAttempt(plan.Portal.entrancePoint.position, true, now))
        {
            approach = plan.Approach;
            return plan.Portal;
        }
        approach = default;
        return null;
    }

    internal void MarkUnsafeEntrance(int entranceId, float now) => entranceSafety.MarkUnsafe(entranceId, now);

    private bool FailedExit(GameNetcodeStuff.PlayerControllerB player, int id, float now)
    {
        return exitFailures.TryGetValue(player, out var state) && state.Failed.TryGetValue(id, out float until) && now < until;
    }

    internal void ExitFailed(IBot bot, int id, float now)
    {
        if (!bot.Inside)
            return;
        if (!exitFailures.TryGetValue(bot.Body, out var state))
        {
            state = new ExitFailureState();
            exitFailures.Add(bot.Body, state);
        }
        state.Failed[id] = now + 45f;
        foreach (var entrance in entrances)
            if (entrance != null && !entrance.isEntranceToBuilding && entrance.entranceId == id && entrance.entrancePoint != null)
            {
                Board.RouteFailed(entrance.entrancePoint.position);
                break;
            }
        escapePlans.Remove(bot);
        bool any = false;
        bool allFailed = true;
        foreach (var entrance in entrances)
        {
            if (entrance == null || entrance.isEntranceToBuilding || entrance.entrancePoint == null)
                continue;
            any = true;
            if (!FailedExit(bot.Body, entrance.entranceId, now))
                allFailed = false;
        }
        if (any && allFailed)
            RequestExitRescue(bot, state, now);
        history.Record(bot, "exit_failed", $"entrance={id} rescue={any && allFailed}");
        dirty = true;
    }

    internal float RecordRouteFailure(IBot bot, int exitId, Vector3 target, string cause, float now)
    {
        if (cause == "door" || cause == "navmesh_partial")
            Board.RouteFailed(target);
        float retryDelay = 8f;
        int attempt = 1;
        if (exitId >= 0)
        {
            if (!exitFailures.TryGetValue(bot.Body, out var state))
            {
                state = new ExitFailureState();
                exitFailures.Add(bot.Body, state);
            }
            state.RouteAttempts.TryGetValue(exitId, out attempt);
            attempt = Mathf.Min(attempt + 1, 16);
            state.RouteAttempts[exitId] = attempt;
            retryDelay = BotRolePolicy.RetryDelay(attempt);
            state.Failed[exitId] = now + retryDelay;
            escapePlans.Remove(bot);
        }
        history.Record(bot, "route_failure",
            $"cause={cause} exit={exitId} attempt={attempt} retry={retryDelay:F0}s target={target}");
        dirty = true;
        return retryDelay;
    }

    internal bool DoorNearRoute(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        float lengthSquared = direction.sqrMagnitude;
        if (lengthSquared < 0.01f)
            return false;
        foreach (var pair in doors)
        {
            var door = pair.Key;
            var trigger = pair.Value;
            if (door == null || trigger == null || !trigger.isBool || trigger.boolValue)
                continue;
            Vector3 offset = door.transform.position - from;
            if (Mathf.Abs(offset.y) > 2.5f)
                continue;
            offset.y = 0f;
            float projection = Mathf.Clamp01(Vector3.Dot(offset, direction) / lengthSquared);
            if ((offset - direction * projection).sqrMagnitude <= 2.25f)
                return true;
        }
        return false;
    }

    internal void NoExitAvailable(IBot bot, float now)
    {
        if (!bot.Inside)
            return;
        if (!exitFailures.TryGetValue(bot.Body, out var state))
        {
            state = new ExitFailureState();
            exitFailures.Add(bot.Body, state);
        }
        if (state.NoExitSince < 0f)
            state.NoExitSince = now;
        if (RecoveryPolicy.NoExitTimedOut(now, state.NoExitSince) && state.RescueUntil < now + 20f)
            RequestExitRescue(bot, state, now);
    }

    private void RequestExitRescue(IBot bot, ExitFailureState state, float now)
    {
        bool newlyRequested = state.RescueUntil <= now;
        state.RescueUntil = now + 45f;
        if (!newlyRequested)
            return;
        history.Record(bot, "rescue_requested", "reason=no_usable_exit");
        if (HUDManager.Instance != null)
            HUDManager.Instance.AddTextToChatOnServer($"{bot.Body.playerUsername}: Exits are blocked; requesting teleport support.");
        dirty = true;
    }

    internal void ExitSelected(IBot bot)
    {
        if (exitFailures.TryGetValue(bot.Body, out var state))
        {
            state.NoExitSince = -1f;
            state.RescueUntil = 0f;
        }
    }

    internal void ExitCompleted(IBot bot, int id)
    {
        foreach (var entrance in entrances)
            if (entrance != null && !entrance.isEntranceToBuilding && entrance.entranceId == id && entrance.entrancePoint != null)
            {
                Board.RouteRecovered(entrance.entrancePoint.position);
                break;
            }
        if (!exitFailures.TryGetValue(bot.Body, out var state))
            return;
        state.Failed.Remove(id);
        state.RouteAttempts.Remove(id);
        state.NoExitSince = -1f;
        state.RescueUntil = 0f;
    }

    internal bool EntranceAllowed(EntranceTeleport entrance, float now) => entranceSafety.IsAllowed(entrance.entranceId, now);

    private bool DestinationEntranceAllowed(Vector3 destination, float now)
    {
        foreach (var entrance in entrances)
        {
            if (entrance != null && entrance.isEntranceToBuilding && entrance.exitScript != null &&
                entrance.exitScript.entrancePoint != null &&
                (entrance.exitScript.entrancePoint.position - destination).sqrMagnitude < 2.25f)
                return EntranceAllowed(entrance, now);
        }
        return true;
    }

    internal void Add(IBot bot)
    {
        bots.Add(bot);
        crewParticipants.Add(bot.Body);
        crewDeaths.Remove(bot.Body);
        dirty = true;
    }

    internal void RememberVisit(GameNetcodeStuff.PlayerControllerB player, Vector3 position, float now)
    {
        if (lastVisits.TryGetValue(player, out var previous))
        {
            Vector3 offset = position - previous;
            float distance = offset.magnitude;
            if (distance < 20f)
            {
                int steps = Mathf.CeilToInt(distance / 1.5f);
                for (int step = 1; step < steps; step++)
                    visits[Vector3Int.FloorToInt((previous + offset * (step / (float)steps)) / 3f)] = now;
            }
        }
        visits[Vector3Int.FloorToInt(position / 3f)] = now;
        lastVisits[player] = position;
    }

    internal bool RecoveryDestinationAllowed(IBot bot, Vector3 point, float now)
    {
        if (Board.Risk(point, true, now, false) >= 0.38f)
            return false;
        foreach (var other in bots)
        {
            if (other != bot && other.Available && other.Inside && other.Order.Kind == TaskKind.Explore &&
                (other.Order.Point - point).sqrMagnitude < 9f)
                return false;
        }
        return true;
    }

    private bool WasVisited(Vector3 point)
    {
        return visits.ContainsKey(Vector3Int.FloorToInt(point / 3f));
    }

    internal void LeaveFacility(GameNetcodeStuff.PlayerControllerB player)
    {
        lastVisits.Remove(player);
        exitFailures.Remove(player);
    }

    internal void BreakTeam(IBot leaving)
    {
        foreach (var member in bots)
        {
            var current = member.Order;
            if (member != leaving && current.Partner != leaving)
                continue;
            if (current.Kind == TaskKind.Escort || member == leaving && current.Inside && !member.Inside)
                member.Assign(new Order(TaskKind.Hold, member.Position, member.Inside));
            else if (current.Partner != null)
                member.Assign(new Order(current.Kind, current.Point, current.Inside, current.Item));
        }
        dirty = true;
    }



    internal void RequestReplan() => dirty = true;

    internal bool DifferentElevatorFloors(Vector3 from, Vector3 to)
    {
        if (Elevator == null || Elevator.elevatorTopPoint == null || Elevator.elevatorBottomPoint == null)
            return false;
        float top = Elevator.elevatorTopPoint.position.y;
        float bottom = Elevator.elevatorBottomPoint.position.y;
        return MineshaftPolicy.DifferentFloors(from.y, to.y, top, bottom);
    }

    internal bool ReserveElevator(IBot bot, float now)
    {
        if (elevatorOwner != null && elevatorOwner != bot && elevatorOwner.Available && now < elevatorLease)
        {
            if (!elevatorSeats.ContainsKey(bot) && !TryJoinElevator(bot, out _))
                return false;
        }
        else
        {
            elevatorOwner = bot;
            elevatorLease = now + 60f;
        }
        if (elevatorSeats.ContainsKey(bot))
            return true;
        int occupied = 0;
        foreach (int seat in elevatorSeats.Values)
            occupied |= 1 << seat;
        int availableSeat = MineshaftPolicy.FreeSeat(occupied);
        if (availableSeat < 0)
            return false;
        elevatorSeats.Add(bot, availableSeat);
        return true;
    }

    internal Vector3 ElevatorBoardingPoint(IBot bot)
    {
        var lift = Elevator!;
        if (!elevatorSeats.TryGetValue(bot, out int seat))
            return lift.elevatorInsidePoint.position;
        Vector3 side = lift.elevatorInsidePoint.right;
        Vector3 forward = lift.elevatorInsidePoint.forward;
        side.y = 0f;
        forward.y = 0f;
        Vector3 point = lift.elevatorInsidePoint.position + side.normalized * ((seat & 1) == 0 ? -0.4f : 0.4f) +
            forward.normalized * (seat < 2 ? 0.4f : -0.4f);
        Vector3 closest = lift.elevatorBounds.bounds.ClosestPoint(point + Vector3.up * 0.5f);
        return new Vector3(closest.x, point.y, closest.z);
    }

    internal bool TryJoinElevator(IBot bot, out Vector3 goal)
    {
        goal = default;
        if (Elevator == null || Elevator.elevatorTopPoint == null || Elevator.elevatorBottomPoint == null ||
            Elevator.elevatorBounds == null || Elevator.elevatorInsidePoint == null || !(elevatorOwner is CrewBot owner) || owner == bot ||
            !owner.Available || !owner.ElevatorBoarding || !bot.Available || !bot.Inside ||
            !elevatorSeats.ContainsKey(bot) && elevatorSeats.Count >= MineshaftPolicy.Seats ||
            (bot.Position - Elevator.elevatorInsidePoint.position).sqrMagnitude > 144f)
            return false;
        goal = owner.ElevatorGoingDown ? Elevator.elevatorBottomPoint.position : Elevator.elevatorTopPoint.position;
        if (!DifferentElevatorFloors(bot.Position, goal))
            return false;
        return bot.Order.Kind == TaskKind.Escort && bot.Order.Partner == owner ||
            DifferentElevatorFloors(bot.Position, bot.Order.Point) &&
            (owner.ElevatorGoingDown ? bot.Order.Point.y < bot.Position.y : bot.Order.Point.y > bot.Position.y);
    }

    internal bool WaitForElevatorCrew(IBot owner, bool goingDown, float boardedAt, float now)
    {
        if (Elevator == null || Elevator.elevatorBounds == null ||
            !MineshaftPolicy.WaitForCrew(owner == elevatorOwner, Board.Risk(owner.Position, true, now, false) < 0.35f,
                true, now - boardedAt))
            return false;
        foreach (var bot in bots)
        {
            if (bot == owner || !bot.Available || !bot.Inside || bot is CrewBot passenger && passenger.ElevatorRiding)
                continue;
            if (TryJoinElevator(bot, out _))
                return true;
        }
        return false;
    }

    internal void ReleaseElevator(IBot bot)
    {
        elevatorSeats.Remove(bot);
        if (elevatorOwner == bot)
        {
            elevatorOwner = null;
            dirty = true;
        }
    }

    internal bool UseElevatorControl(IBot bot, bool call, bool callDown, float now)
    {
        if (Elevator == null || elevatorOwner != bot || now < nextElevatorAction || !Elevator.elevatorFinishedMoving)
            return false;
        if (call)
        {
            if (Elevator.elevatorCalled)
                return false;
            foreach (var player in round.allPlayerScripts)
            {
                if (player != null && player.isPlayerControlled && !player.isPlayerDead && !BotBody.Contains(player) &&
                    Elevator.elevatorBounds != null && Elevator.elevatorBounds.bounds.Contains(player.transform.position))
                    return false;
            }
            Elevator.CallElevator(callDown);
        }
        else
            Elevator.PressElevatorButton();
        nextElevatorAction = now + 3f;
        return true;
    }

    internal void Staged(IBot bot, GrabbableObject item)
    {
        staged[item] = (bot, Time.time);
        dirty = true;
    }

    internal void StashNoisyLoot(IBot bot, GrabbableObject item, float now)
    {
        noisyLoot[item] = (bot, now);
        history.Record(bot, "noisy_loot", $"dropped={item.name} position={bot.Position}");
        dirty = true;
    }

    internal void StashEscapeLoot(IBot bot, GrabbableObject item, float now)
    {
        noisyLoot[item] = (bot, now);
        history.Record(bot, "escape_loot", $"dropped={item.name} position={bot.Position}");
        dirty = true;
    }

    private bool NoisyLootReady(GrabbableObject item, float now)
    {
        if (!noisyLoot.TryGetValue(item, out var stored))
            return true;
        bool unsafeNearby = Board.DogNearPosition(item.transform.position, item.isInFactory) ||
            Board.Risk(item.transform.position, item.isInFactory, now, false) >= 0.35f;
        if (unsafeNearby)
        {
            noisyLoot[item] = (stored.Owner, now);
        }
        return RecoveryPolicy.NoisyLootReady(now, stored.LastDog, unsafeNearby);
    }

    private bool TryRecoverNoisyLoot(IBot bot, float now, out GrabbableObject result)
    {
        result = null!;
        if (!BotBody.TryGet(bot.Body, out var carrier))
            return false;
        noisyLootKeys.Clear();
        foreach (var key in noisyLoot.Keys)
            noisyLootKeys.Add(key);
        foreach (var item in noisyLootKeys)
        {
            if (item == null || !item.IsSpawned)
            {
                noisyLoot.Remove(item!);
                continue;
            }
            if (item.isHeld || item.heldByPlayerOnServer || item.isHeldByEnemy || item.isInShipRoom)
            {
                noisyLoot.Remove(item);
                continue;
            }
            if (!noisyLoot.TryGetValue(item, out var entry) || entry.Owner != bot || !IsAvailable(item) ||
                item.isHeldByEnemy || item.isInFactory != bot.Inside || !NoisyLootReady(item, now) ||
                !carrier.CanCarryAfterPocket(item) || reserved.Contains(item) ||
                lootOwners.TryGetValue(item, out var owner) && owner != bot ||
                IsLate() && (item.isInFactory || (item.transform.position - ship).sqrMagnitude > LateScrapRadius * LateScrapRadius) ||
                (item.transform.position - bot.Position).sqrMagnitude > 4900f ||
                Board.Risk(item.transform.position, bot.Inside, now) >= 0.35f ||
                !bot.CanAttempt(item.transform.position, bot.Inside, now) || !bot.CanReach(item.transform.position, bot.Inside))
                continue;
            result = item;
            return true;
        }
        return false;
    }

    private bool HumanAboard()
    {
        foreach (var player in round.allPlayerScripts)
        {
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && !BotBody.Contains(player) && player.isInHangarShipRoom)
                return true;
        }
        return false;
    }

    internal void Remove(IBot bot, bool died)
    {
        if (!bots.Remove(bot))
            return;
        lastVisits.Remove(bot.Body);
        escapePlans.Remove(bot);
        exitFailures.Remove(bot.Body);
        hiddenEntryRetry.Remove(bot);
        if (died)
        {
            crewDeaths.Add(bot.Body);
            var body = bot is CrewBot crew ? crew.PhysicalBody : null;
            Board.RecordDeath(bot.Position, bot.Inside, Time.time);
            if (bot.Inside)
                foreach (var entrance in entrances)
                    if (entrance != null && !entrance.isEntranceToBuilding && entrance.entrancePoint != null &&
                        (entrance.entrancePoint.position - bot.Position).sqrMagnitude <= 100f)
                        MarkUnsafeEntrance(entrance.entranceId, Time.time);
            history.Record(bot, "death", body != null && body.Dead ? $"cause={body.DeathCause} attacker={body.DeathAttacker} body={body.SpawnBody}" : "cause=unknown attacker=unknown");
            if (guardian != null && guardian.Available && guardian.Aboard && HUDManager.Instance != null)
            {
                string cause = body != null && body.Dead ? $"{body.DeathCause}, {body.DeathAttacker}" : "unknown cause";
                HUDManager.Instance.AddTextToChatOnServer($"{guardian.Body.playerUsername}: {bot.Body.playerUsername} died ({cause}); marking the area dangerous.");
            }
            Outcome(bot, -1f);
        }
        if (guardian == bot)
            guardian = null;
        dirty = true;
    }

    internal void Outcome(IBot bot, float reward, bool routeFailure = false)
    {
        if (bot.Order.Kind != TaskKind.Hold)
        {
            history.Record(bot, routeFailure ? "route_failed" : "outcome", $"reward={reward:F2} duration={Mathf.Max(0f, Time.time - bot.AssignedAt):F1}");
            float elapsed = Mathf.Max(0f, Time.time - bot.AssignedAt);
            learning.Observe("task:" + bot.Order.Kind, reward > 0f ? reward / (1f + elapsed / 30f) : reward);
        }

        var threat = Board.Nearby(bot.Position, bot.Inside, Time.time);
        if (threat != null)
            learning.Observe(threat.Key, reward);

        if (routeFailure)
        {
            if (bot.Cargo == null && bot.Order.Kind != TaskKind.Return && bot.Order.Kind != TaskKind.Guard && !IsLate())
                Board.RouteFailed(bot.Order.Point);
            learning.Observe(bot.Inside ? "route:inside" : "route:outside", -0.6f);
        }
        else if (reward > 0f && bot.Order.Kind != TaskKind.Evade)
        {
            learning.Observe(bot.Inside ? "route:inside" : "route:outside", 0.25f);
            visits[Vector3Int.FloorToInt(bot.Position / (bot.Inside ? 3f : 8f))] = Time.time;
        }

        dirty = true;
    }

    internal void Validate(float now)
    {
        if (!dirty && now < nextValidation)
            return;

        dirty = false;
        nextValidation = now + ValidationInterval;
        foreach (var player in round.allPlayerScripts)
        {
            if (player == null)
                continue;
            if (player.isPlayerControlled && !player.isPlayerDead && !player.disconnectedMidGame)
            {
                crewParticipants.Add(player);
                crewDeaths.Remove(player);
            }
            else if (player.isPlayerDead && !player.disconnectedMidGame &&
                (crewParticipants.Contains(player) || player.playerClientId <= int.MaxValue && round.ClientPlayerList.ContainsValue((int)player.playerClientId)))
            {
                crewParticipants.Add(player);
                crewDeaths.Add(player);
            }
            else if (player.disconnectedMidGame)
                crewDeaths.Remove(player);
        }
        if (now >= nextEntranceRefresh)
        {
            nextEntranceRefresh = now + 10f;
            if (teleporter == null)
                FindTeleporter();
            if (charger == null)
                charger = UnityEngine.Object.FindObjectOfType<ItemCharger>();
            if (shipTerminal == null)
                shipTerminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            if (Elevator == null)
                Elevator = UnityEngine.Object.FindObjectOfType<MineshaftElevatorController>();
            if (dropship == null)
                dropship = UnityEngine.Object.FindObjectOfType<ItemDropship>();
            if (insideNodes.Count == 0)
                ReadNodes("AINode", insideNodes);
            foreach (var entrance in UnityEngine.Object.FindObjectsOfType<EntranceTeleport>())
            {
                if (entrance != null && entrance.entrancePoint != null && !entrances.Contains(entrance))
                    entrances.Add(entrance);
            }
            entrances.RemoveAll(entrance => entrance == null);
            foreach (var terminal in UnityEngine.Object.FindObjectsOfType<TerminalAccessibleObject>())
            {
                if (terminal != null && !terminalDevices.Contains(terminal))
                    terminalDevices.Add(terminal);
            }
            terminalDevices.RemoveAll(terminal => terminal == null);
        }
        foreach (var pair in doorObstacles)
        {
            if (pair.Key != null && pair.Value.Obstacle != null && !pair.Key.isLocked && pair.Value.Obstacle.carving)
                pair.Value.Obstacle.carving = false;
        }
        bool someoneInside = false;
        foreach (var player in round.allPlayerScripts)
        {
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && player.isInsideFactory)
            {
                someoneInside = true;
                break;
            }
        }
        facilityCrewPresent = someoneInside;
        if (!facilityCrewPresent)
            foreach (var bot in bots)
                if (bot.Available && bot.Inside)
                {
                    facilityCrewPresent = true;
                    break;
                }
        if (!facilityCrewPresent && BotSettings.RecoverBodies)
            foreach (var player in round.allPlayerScripts)
                if (RecoverableBody(player))
                {
                    facilityCrewPresent = true;
                    break;
                }
        var monitor = round.mapScreen;
        bool hasRadarOperator = facilityCrewPresent && guardian != null && guardian.Available && guardian.Aboard;
        if (facilityCrewPresent && BotSettings.ShipSupport && !hasRadarOperator)
        {
            foreach (var bot in bots)
            {
                if (bot.Available && bot.Aboard && bot.Cargo == null &&
                    (bot.Order.Kind == TaskKind.Hold || bot.Order.Kind == TaskKind.Guard || bot.Order.Kind == TaskKind.Return || bot.Order.Kind == TaskKind.ShipControl))
                {
                    hasRadarOperator = true;
                    break;
                }
            }
        }
        var radarFocus = BotSettings.ShipSupport && hasRadarOperator && monitor != null &&
            !monitor.currentCameraDisabled ? monitor.targetedPlayer : null;
        foreach (var player in round.allPlayerScripts)
        {
            if (player == null || BotBody.Contains(player))
                continue;
            if (player.isPlayerControlled && !player.isPlayerDead && player.isInsideFactory)
                RememberVisit(player, player.transform.position, now);
            else
                lastVisits.Remove(player);
        }
        bool wasCoordinated = Board.Coordinated;
        Board.Refresh(bots, learning, now, radarFocus);
        RefreshQuota(now);
        if (wasCoordinated && !Board.Coordinated)
        {
            foreach (var bot in bots)
            {
                var order = bot.Order;
                if (order.Partner == null)
                    continue;
                bot.Assign(order.Kind == TaskKind.Escort
                    ? new Order(TaskKind.Hold, bot.Position, bot.Inside)
                    : new Order(order.Kind, order.Point, order.Inside, order.Item));
            }
        }
        foreach (var bot in bots)
            PlanEscape(bot, now);
        scrap = Board.ScavengableItems(bots);
        RefreshEntranceBatches();
        if (now >= nextSave)
        {
            nextSave = now + 60f;
            learning.Save();
        }

        available.Clear();
        assigned.Clear();
        reserved.Clear();
        foreach (var bot in bots)
        {
            if (bot.Available)
                available.Add(bot);
        }

        RefreshRoles();

        if (TryBotDeparture(now))
            return;

        foreach (var bot in available)
        {
            if (bot.ElevatorTransit)
                assigned.Add(bot);
        }

        GuardShip(now);
        AssignPeeperRescue(now);
        AssignEquipmentHandoffs(now);
        IBot? shipOperator = facilityCrewPresent && guardian != null && guardian.Available && guardian.Aboard ? guardian : null;
        if (shipOperator != null && shipOperator.Order.Kind == TaskKind.AssistPeeper)
            shipOperator = null;
        if (facilityCrewPresent && BotSettings.ShipSupport && shipOperator == null)
        {
            foreach (var bot in available)
            {
                if (bot.Aboard && bot.Cargo == null && bot.Order.Kind != TaskKind.AssistPeeper &&
                    bot.Order.Kind != TaskKind.EquipWeapon && bot.Order.Kind != TaskKind.CareBaby && !assigned.Contains(bot))
                {
                    if (shipOperator == null || bot.Body.health < shipOperator.Body.health)
                        shipOperator = bot;
                }
            }
        }
        if (BotSettings.ShipSupport)
        {
            doorOperator = null;
            OperateShipDoor(now);
            if (shipOperator == doorOperator)
                shipOperator = null;
            IBot? lightsOperator = shipOperator;
            if (lightsOperator == null)
                foreach (var bot in available)
                    if (bot.Aboard && bot.Cargo == null && bot != doorOperator &&
                        bot.Order.Kind != TaskKind.AssistPeeper && RoleOf(bot) == BotRole.ShipSupport)
                    {
                        lightsOperator = bot;
                        break;
                    }
            if (lightsOperator != null && RestoreShipLights(lightsOperator, now) && shipOperator == lightsOperator)
                shipOperator = null;
        }
        if (facilityCrewPresent && BotSettings.ShipSupport && teleporter != null && teleporter.isActiveAndEnabled &&
            shipOperator == null && HasExitRescueRequest(now))
        {
            IBot? replacement = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var bot in available)
            {
                if (assigned.Contains(bot) || bot.Inside || bot.Aboard || bot.Cargo != null ||
                    Board.Risk(bot.Position, false, now, false) >= 0.35f || !bot.CanReach(ship, false))
                    continue;
                float distance = (bot.Position - ship).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    replacement = bot;
                }
            }
            if (replacement != null)
                Assign(replacement, new Order(TaskKind.Guard, ship));
        }
        bool terminalInUse = false;
        if (!facilityCrewPresent)
            rescueTarget = null;
        bool terminalReady = false;
        if (facilityCrewPresent && BotSettings.ShipSupport && shipOperator != null)
        {
            foreach (var player in round.allPlayerScripts)
                if (player != null && player.isPlayerControlled && !BotBody.Contains(player) && player.inTerminalMenu)
                    terminalInUse = true;
            if (shipTerminal != null && shipTerminal.terminalInUse)
                terminalInUse = true;
            if (!terminalInUse)
                OperateTeleporter(now, shipOperator);
            else
                rescueTarget = null;
            if (!terminalInUse && rescueTarget == null && shipTerminal != null)
            {
                var trigger = TerminalTrigger(shipTerminal) ?? shipTerminal.GetComponent<InteractTrigger>();
                if (trigger != null)
                {
                    Vector3 terminalPoint = trigger.playerPositionNode != null ? trigger.playerPositionNode.position : trigger.transform.position;
                    if (ApproachTerminal(shipOperator, terminalPoint))
                    {
                        shipControlUntil = now + 1.5f;
                        Assign(shipOperator, new Order(TaskKind.ShipControl, shipOperator.Position));
                        terminalReady = true;
                    }
                }
            }
            if (terminalReady && rescueTarget == null && round.mapScreen != null && now >= nextRadarSwitch)
            {
                nextRadarSwitch = now + 15f;
                if (round.mapScreen.currentCameraDisabled)
                {
                    round.mapScreen.SwitchScreenOn(true);
                    round.mapScreen.SwitchScreenOnServerRpc(true);
                }
                if (round.mapScreen.radarTargets.Count > 1)
                    round.mapScreen.SwitchRadarTargetForward(true);
            }
            if (terminalReady && now >= nextShipSupport)
            {
                nextShipSupport = now + 5f;
                if (!Board.ShipSupport(available))
                {
                    foreach (var terminal in terminalDevices)
                    {
                        if (terminal == null || !terminal.isBigDoor || string.IsNullOrEmpty(terminal.objectCode) ||
                            !terminal.TryGetComponent<AnimatedObjectTrigger>(out var door) || door.boolValue)
                            continue;
                        foreach (var player in round.allPlayerScripts)
                        {
                            if (player == null || !player.isPlayerControlled || player.isPlayerDead || !player.isInsideFactory ||
                                (player.transform.position - terminal.transform.position).sqrMagnitude > 25f ||
                                Board.Risk(terminal.transform.position, true, now) >= 0.35f)
                                continue;
                            terminal.SetDoorLocalClient(true);
                            break;
                        }
                    }
                }
            }
        }
        var terminalTrigger = shipTerminal != null ? TerminalTrigger(shipTerminal) ?? shipTerminal.GetComponent<InteractTrigger>() : null;
        foreach (var bot in bots)
            if (bot is CrewBot crew)
                crew.SetTerminalUsing(terminalReady && bot == shipOperator,
                    terminalTrigger != null ? terminalTrigger.transform.position : ship,
                    terminalTrigger != null && terminalTrigger.playerPositionNode != null ? terminalTrigger.playerPositionNode.rotation : null);
        if (EarlyDepartureVote)
        {
            foreach (var bot in available)
            {
                if (bot.ElevatorTransit || bot.Aboard && bot.Order.Kind == TaskKind.ShipControl)
                    continue;
                Assign(bot, new Order(bot.Cargo != null ? TaskKind.Transport : TaskKind.Return, ship));
            }
            return;
        }
        AssignDropshipDelivery(now);
        float secondsLeft = SecondsUntilDeparture();
        if (secondsLeft <= 180f)
            foreach (var bot in available)
                if (!assigned.Contains(bot) && MustReturnNow(bot, secondsLeft, now))
                    Assign(bot, new Order(TaskKind.Return, ship));
        if (IsLate())
        {
            AllocateLate(now);
            return;
        }
        lootOwners.Clear();
        foreach (var bot in available)
        {
            var current = bot.Order;
            if (current.Kind == TaskKind.Scavenge && IsAvailable(current.Item) && !lootOwners.ContainsKey(current.Item!))
                lootOwners.Add(current.Item!, bot);
        }
        foreach (var bot in available)
        {
            if (assigned.Contains(bot))
                continue;

            if (Board.NearbyFleeing(bot.Position, bot.Inside, bot.Body, now) is Blackboard.Contact contact &&
                !CanCalmSlime(bot, contact, now) && Blackboard.MustFlee(contact, bot.Body) && ((bot.Position - contact.Sample.Position).sqrMagnitude < 81f || contact.Sample.Pursuing == bot.Body))
            {
                Assign(bot, new Order(TaskKind.Evade, Vector3.zero, bot.Inside));
            }
            else if (BotSettings.ShipSupport && RoleOf(bot) == BotRole.ShipSupport)
            {
                Assign(bot, new Order(bot.Cargo != null ? TaskKind.Transport : TaskKind.Guard, ship));
            }
            else if (TryRecoverNoisyLoot(bot, now, out var recoverable))
                Assign(bot, new Order(TaskKind.Scavenge, recoverable.transform.position, bot.Inside, recoverable));
            else if (bot.Order.Kind == TaskKind.Stage && !bot.Inside)
                Assign(bot, bot.Order);
            else if (bot.Cargo != null)
            {
                if (TryNearbyCargo(bot, now, out var item))
                    Assign(bot, new Order(TaskKind.Scavenge, item.transform.position, bot.Inside, item));
                else if (bot.Inside && !(bot.Cargo is RagdollGrabbableObject))
                    Assign(bot, new Order(TaskKind.Stage, ship));
                else
                    Assign(bot, new Order(TaskKind.Transport, ship));
            }
        }

        AssignBabyCare(now);

        explorationOwners.Clear();
        foreach (var bot in available)
        {
            if (assigned.Contains(bot))
                continue;
            var current = bot.Order;
            if (!bot.CanAttempt(current.Point, current.Inside, now))
                continue;
            if (current.Kind == TaskKind.Scavenge && IsAvailable(current.Item) && !lootOwners.ContainsKey(current.Item!))
                lootOwners.Add(current.Item!, bot);
            else if (current.Kind == TaskKind.Explore)
                explorationOwners.Add(bot);
        }

        if (!Board.Coordinated)
        {
            foreach (var bot in available)
            {
                if (!assigned.Contains(bot))
                    Allocate(bot, null, now);
            }
            return;
        }

        foreach (var bot in available)
        {
            if (!bot.Inside && !assigned.Contains(bot))
                Allocate(bot, null, now);
        }

        // Preserve an existing viable team before filling new pairs.
        foreach (var leader in available)
        {
            if (!leader.Inside || assigned.Contains(leader) || leader.Order.Kind == TaskKind.Escort)
                continue;
            var partner = leader.Order.Partner;
            if (partner != null && partner.Available && partner.Inside && !assigned.Contains(partner) && partner.Order.Kind == TaskKind.Escort && partner.Order.Partner == leader)
                Allocate(leader, partner, now);
        }

        foreach (var leader in available)
        {
            if (assigned.Contains(leader))
                continue;

            IBot? partner = null;
            float nearest = float.PositiveInfinity;
            foreach (var candidate in available)
            {
                if (candidate == leader || !candidate.Inside || assigned.Contains(candidate))
                    continue;
                float distance = (candidate.Position - leader.Position).sqrMagnitude;
                if (distance < nearest)
                {
                    partner = candidate;
                    nearest = distance;
                }
            }

            Allocate(leader, partner, now);
        }
    }

    private void FindTeleporter()
    {
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ShipTeleporter>())
            if (candidate != null && !candidate.isInverseTeleporter)
            {
                teleporter = candidate;
                return;
            }
    }

    private bool TryBotDeparture(float now)
    {
        bool eligible = IsLate() && !round.inShipPhase && round.shipHasLanded && !round.shipIsLeaving && available.Count > 0;
        foreach (var player in round.allPlayerScripts)
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && !BotBody.Contains(player))
                eligible = false;
        foreach (var bot in available)
            if (!bot.Aboard)
                eligible = false;
        if (PeeperInShip())
            eligible = false;
        if (SecondsUntilDeparture() > 45f)
            foreach (var player in round.allPlayerScripts)
                if (RecoverableBody(player))
                    eligible = false;
        if (!eligible || departureLever == null || departureLever.triggerScript == null)
        {
            if (departureBot != null)
                departureBot.SetLeverUsing(false);
            departureBot = null;
            departureAnimationUntil = 0f;
            return false;
        }
        if (departureBot == null || !departureBot.Available || !departureBot.Aboard)
        {
            departureBot = null;
            foreach (var bot in available)
                if (bot is CrewBot candidate)
                {
                    departureBot = candidate;
                    break;
                }
        }
        if (departureBot == null)
            return false;
        var trigger = departureLever.triggerScript;
        Vector3 point = trigger.playerPositionNode != null ? trigger.playerPositionNode.position : trigger.transform.position;
        if (!ApproachShipControl(departureBot, point))
            return true;
        Assign(departureBot, new Order(TaskKind.ShipControl, departureBot.Position));
        if (departureAnimationUntil <= 0f)
        {
            departureBot.SetTerminalUsing(false, point);
            departureBot.SetLeverUsing(true);
            departureAnimationUntil = now + 1.5f;
            return true;
        }
        if (now < departureAnimationUntil)
            return true;
        departureBot.SetLeverUsing(false);
        departureLever.PlayLeverPullEffectsServerRpc(false);
        departureLever.EndGame();
        departureAnimationUntil = 0f;
        return true;
    }

    private void AssignPeeperRescue(float now)
    {
        if (!BotSettings.ShipSupport)
            return;
        foreach (var target in round.allPlayerScripts)
        {
            if (target == null || !target.isPlayerControlled || target.isPlayerDead || target.health <= 1 || !target.isInHangarShipRoom)
                continue;
            bool peeper = PeeperCompatibility.HasAttached(target);
            bool tulip = Blackboard.AttachedTulip(target) != null;
            if (!peeper && !tulip)
                continue;
            IBot? rescuer = null;
            BotBody? rescuerBody = null;
            float best = float.PositiveInfinity;
            foreach (var bot in available)
            {
                if (!bot.Aboard || bot.Body == target || bot.Cargo != null ||
                    !BotBody.TryGet(bot.Body, out var carrier) || carrier.FindItem<Shovel>() is not Shovel shovel || tulip && !CrewBot.SafeTulipSwing(target, shovel))
                    continue;
                float distance = (bot.Position - target.transform.position).sqrMagnitude;
                if (bot == guardian)
                    distance -= 100f;
                if (distance >= best)
                    continue;
                rescuer = bot;
                rescuerBody = carrier;
                best = distance;
            }
            if (rescuer is CrewBot crew && rescuerBody != null)
            {
                crew.AssignPeeperTarget(target, !peeper && tulip);
                Assign(crew, new Order(TaskKind.AssistPeeper, target.transform.position));
                return;
            }
            foreach (var bot in available)
            {
                if (!bot.Aboard || bot.Body == target || bot.Cargo != null || !BotBody.TryGet(bot.Body, out var carrier))
                    continue;
                foreach (var item in Blackboard.Items)
                {
                    if (!(item is Shovel shovel) || tulip && !CrewBot.SafeTulipSwing(target, shovel) || !item.isInShipRoom || !item.IsSpawned || !item.grabbable || item.isHeld ||
                        item.heldByPlayerOnServer || item.isHeldByEnemy || !CanEquip(item, now) || !carrier.CanCarryAfterPocket(item) ||
                        !bot.CanReach(item.transform.position, false))
                        continue;
                    Assign(bot, new Order(TaskKind.EquipWeapon, item.transform.position, false, item));
                    return;
                }
            }
        }
    }

    private void AssignDropshipDelivery(float now)
    {
        foreach (var item in DeliveryRegistry.Items)
        {
            if (item == null || !item.IsSpawned || !item.isHeld || item.playerHeldBy == null)
                continue;
            foreach (var bot in available)
            {
                if (bot.Body != item.playerHeldBy || bot.ElevatorTransit || bot.Inside)
                    continue;
                Assign(bot, new Order(TaskKind.DeliverDelivery, ship, false, item));
                return;
            }
        }
        if (!BotSettings.CollectDeliveries || dropship == null)
            return;

        GrabbableObject? selectedItem = null;
        IBot? selectedBot = null;
        float best = float.PositiveInfinity;
        foreach (var item in DeliveryRegistry.Items)
        {
            if (item == null || !item.IsSpawned || !item.isActiveAndEnabled || !item.grabbable || item.itemProperties == null ||
                item.isHeld || item.heldByPlayerOnServer || item.isHeldByEnemy || item.isInShipRoom || LightningHazard.IsTarget(item))
                continue;
            foreach (var bot in available)
            {
                if (assigned.Contains(bot) || bot.Inside || bot.Cargo != null || !BotBody.TryGet(bot.Body, out var carrier) ||
                    !carrier.CanCarryAfterPocket(item))
                    continue;
                float distance = (bot.Position - item.transform.position).sqrMagnitude;
                if (!DeliveryPolicy.PreferDistance(distance, best) || Board.Risk(item.transform.position, false, now) >= 0.38f ||
                    !bot.CanAttempt(item.transform.position, false, now) || !bot.CanReach(item.transform.position, false))
                    continue;
                selectedBot = bot;
                selectedItem = item;
                best = distance;
            }
        }
        if (selectedBot != null && selectedItem != null)
        {
            Assign(selectedBot, new Order(TaskKind.CollectDelivery, selectedItem.transform.position, false, selectedItem));
            return;
        }
        if (!DeliveryPolicy.ShouldOpen(BotSettings.CollectDeliveries, dropship.shipLanded, dropship.shipDoorsOpened,
            DropshipOrders(dropship).Count) || dropship.triggerScript == null)
            return;
        Vector3 target = dropship.triggerScript.transform.position;
        if (!NavMesh.SamplePosition(target, out var approach, 3f, NavMesh.AllAreas))
            return;
        foreach (var bot in available)
        {
            if (assigned.Contains(bot) || bot.Inside || bot.Cargo != null || !BotBody.TryGet(bot.Body, out var carrier) ||
                carrier.FreeScrapSlots() <= 0 && bot.Body.ItemOnlySlot != null)
                continue;
            float distance = (bot.Position - approach.position).sqrMagnitude;
            if (!DeliveryPolicy.PreferDistance(distance, best) || Board.Risk(approach.position, false, now) >= 0.38f ||
                !bot.CanAttempt(approach.position, false, now) || !bot.CanReach(approach.position, false))
                continue;
            selectedBot = bot;
            best = distance;
        }
        if (selectedBot != null)
            Assign(selectedBot, new Order(TaskKind.OpenDelivery, approach.position));
    }

    private void OperateShipDoor(float now)
    {
        if (hangarDoor == null || !hangarDoor.isActiveAndEnabled || round.shipIsLeaving || !round.shipHasLanded)
            return;
        if (!round.hangarDoorsClosed)
            automaticDoorClosed = false;
        IBot? operatorBot = null;
        foreach (var bot in available)
            if (bot.Available && bot.Aboard && bot.Cargo == null && bot.Order.Kind != TaskKind.AssistPeeper)
            {
                operatorBot = bot;
                break;
            }
        if (operatorBot == null || !hangarDoor.buttonsEnabled || now < nextDoorAction)
            return;
        Vector3 doorPoint = hangarDoor.outsideDoorPoint != null ? hangarDoor.outsideDoorPoint.position : ship;
        bool threat = Board.ThreatNearShip(doorPoint, now);
        if (threat)
            lastDoorThreat = now;
        if (hangarDoor.triggerScript != null)
            foreach (var player in round.allPlayerScripts)
                if (player != null && player.isPlayerControlled && !player.isPlayerDead && !BotBody.Contains(player) &&
                    (player.transform.position - hangarDoor.triggerScript.transform.position).sqrMagnitude < 4f)
                    return;
        if (automaticDoorClosed && round.hangarDoorsClosed &&
            (EarlyDepartureVote || TeamDecisionPolicy.ShouldOpenDoor(threat, now - lastDoorThreat, hangarDoor.doorPower)))
        {
            if (!PrepareShipDoor(operatorBot, doorPoint, now))
                return;
            round.SetShipDoorsClosed(false);
            round.SetDoorsClosedServerRpc(false);
            automaticDoorClosed = false;
            nextDoorAction = now + 3f;
            history.Record(operatorBot, "ship_door", "opened");
            return;
        }
        if (EarlyDepartureVote || round.hangarDoorsClosed ||
            hangarDoor.triggerScript == null || !hangarDoor.triggerScript.interactable)
            return;
        bool crewNearDoor = false;
        foreach (var player in round.allPlayerScripts)
        {
            if (player == null || !player.isPlayerControlled || player.isPlayerDead || player.isInsideFactory || player.isInHangarShipRoom)
                continue;
            if ((player.transform.position - doorPoint).sqrMagnitude < 64f)
            {
                crewNearDoor = true;
                break;
            }
        }
        if (!TeamDecisionPolicy.ShouldCloseDoor(threat, crewNearDoor, hangarDoor.overheated, hangarDoor.doorPower))
            return;
        if (!PrepareShipDoor(operatorBot, doorPoint, now))
            return;
        round.SetShipDoorsClosed(true);
        round.SetDoorsClosedServerRpc(true);
        automaticDoorClosed = true;
        nextDoorAction = now + 3f;
        history.Record(operatorBot, "ship_door", "closed_nearby_threat");
    }

    private bool PrepareShipDoor(IBot bot, Vector3 doorPoint, float now)
    {
        if (hangarDoor == null || hangarDoor.triggerScript == null)
            return false;
        doorOperator = bot;
        if (!ApproachShipControl(bot, hangarDoor.triggerScript.transform.position))
            return false;
        shipControlUntil = now + 1.5f;
        Assign(bot, new Order(TaskKind.ShipControl, bot.Position));
        return !(bot is CrewBot crew) || crew.FaceShipDoor(doorPoint, now);
    }

    private bool RestoreShipLights(IBot bot, float now)
    {
        var lights = round.shipRoomLights;
        if (lights == null || lights.areLightsOn || now < nextLightsAction || round.shipIsLeaving)
            return false;
        if (lightsSwitch == null && now >= nextLightsSearch)
        {
            nextLightsSearch = now + 10f;
            foreach (var trigger in UnityEngine.Object.FindObjectsOfType<InteractTrigger>())
            {
                if (trigger == null || !round.shipBounds.bounds.Contains(trigger.transform.position))
                    continue;
                if (ControlsShipLights(trigger.onInteract, lights) || ControlsShipLights(trigger.onInteractEarly, lights))
                    lightsSwitch = trigger;
                if (lightsSwitch != null)
                    break;
            }
        }
        if (lightsSwitch == null || !lightsSwitch.interactable)
            return false;
        if (!ApproachShipControl(bot, lightsSwitch.transform.position))
            return true;
        Assign(bot, new Order(TaskKind.ShipControl, bot.Position));
        shipControlUntil = now + 1.5f;
        lights.SetShipLightsBoolean(true);
        nextLightsAction = now + 5f;
        Record(bot, "ship_lights", "turned_on");
        return true;
    }

    private static bool ControlsShipLights(InteractEvent interaction, ShipLights lights)
    {
        if (interaction == null)
            return false;
        for (int index = 0; index < interaction.GetPersistentEventCount(); index++)
        {
            string method = interaction.GetPersistentMethodName(index);
            if (interaction.GetPersistentTarget(index) == lights &&
                (method == "ToggleShipLights" || method == "SetShipLightsBoolean"))
                return true;
        }
        return false;
    }

    private bool HasExitRescueRequest(float now)
    {
        foreach (var entry in exitFailures)
            if (entry.Key != null && entry.Key.isPlayerControlled && !entry.Key.isPlayerDead && entry.Key.isInsideFactory &&
                now < entry.Value.RescueUntil)
                return true;
        return false;
    }

    private bool NeedsRescue(GameNetcodeStuff.PlayerControllerB player, float now)
    {
        if (player != null && recentTeleports.TryGetValue(player, out float lastTeleport) && now - lastTeleport < 30f)
            return false;
        if (RecoverableBody(player))
            return true;
        if (player == null || !player.isPlayerControlled || player.isPlayerDead || !player.isInsideFactory ||
            player.isInHangarShipRoom || player.redirectToEnemy != null)
            return false;
        if (player.inAnimationWithEnemy != null && !player.inAnimationWithEnemy.isEnemyDead)
            return true;
        if (exitFailures.TryGetValue(player, out var failure) && now < failure.RescueUntil && BotBody.Contains(player))
            return true;
        var turret = Board.DangerousTurret(player.transform.position, true, player);
        if (turret != null && player.health <= 50 &&
            (turret.turretMode == TurretMode.Firing || turret.turretMode == TurretMode.Berserk))
            return true;
        var threat = Board.NearbyFleeing(player.transform.position, true, player, now);
        if (threat == null)
            return false;
        float distance = (threat.Sample.Position - player.transform.position).sqrMagnitude;
        return distance < 12.25f || player.health <= 35 && distance < 64f && threat.Sample.Pursuing == player;
    }

    private float RescuePriority(GameNetcodeStuff.PlayerControllerB player, float now)
    {
        if (player.isPlayerDead)
            return -100f;
        bool grabbed = player.inAnimationWithEnemy != null && !player.inAnimationWithEnemy.isEnemyDead;
        bool trapped = exitFailures.TryGetValue(player, out var failure) && now < failure.RescueUntil && BotBody.Contains(player);
        bool turret = Board.DangerousTurret(player.transform.position, true, player) != null;
        var threat = Board.NearbyFleeing(player.transform.position, true, player, now);
        float distance = threat != null ? Vector3.Distance(threat.Sample.Position, player.transform.position) : float.PositiveInfinity;
        return TeamDecisionPolicy.RescueScore(player.health, grabbed, trapped, turret, distance,
            threat != null && threat.Sample.Pursuing == player);
    }

    private bool ApproachShipControl(IBot bot, Vector3 button)
    {
        Vector3 offset = button - (bot.Position + Vector3.up);
        if (offset.sqrMagnitude <= 3.24f)
            return true;
        if (NavMesh.SamplePosition(button, out var point, 2f, NavMesh.AllAreas) &&
            round.shipInnerRoomBounds.bounds.Contains(point.position + Vector3.up * 0.45f))
        {
            shipControlUntil = Time.time + 1.5f;
            Assign(bot, new Order(TaskKind.ShipControl, point.position));
        }
        return false;
    }

    private bool ApproachTerminal(IBot bot, Vector3 position)
    {
        if (!NavMesh.SamplePosition(position, out var point, 2f, NavMesh.AllAreas) ||
            !round.shipInnerRoomBounds.bounds.Contains(point.position + Vector3.up * 0.45f))
            return false;
        Vector3 offset = point.position - bot.Position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= 0.64f)
            return true;
        shipControlUntil = Time.time + 1.5f;
        Assign(bot, new Order(TaskKind.ShipControl, point.position));
        return false;
    }

    private void OperateTeleporter(float now, IBot shipOperator)
    {
        var monitor = round.mapScreen;
        if (teleporter == null || !teleporter.isActiveAndEnabled || teleporter.buttonTrigger == null ||
            !teleporter.buttonTrigger.interactable || monitor == null || round.inShipPhase || round.shipIsLeaving)
        {
            rescueTarget = null;
            return;
        }
        GameNetcodeStuff.PlayerControllerB? best = null;
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;
        foreach (var player in round.allPlayerScripts)
        {
            if (!NeedsRescue(player, now))
                continue;
            float score = RescuePriority(player, now);
            if (score <= bestScore)
                continue;
            for (int index = 0; index < monitor.radarTargets.Count; index++)
            {
                if (monitor.radarTargets[index] == null || monitor.radarTargets[index].transform == null || monitor.radarTargets[index].transform != player.transform &&
                    (player.deadBody == null || monitor.radarTargets[index].transform != player.deadBody.radarDot &&
                    !monitor.radarTargets[index].transform.IsChildOf(player.deadBody.transform)))
                    continue;
                best = player;
                bestIndex = index;
                bestScore = score;
                break;
            }
        }
        if (rescueTarget != null)
        {
            if (now >= rescueDeadline || !NeedsRescue(rescueTarget, now))
            {
                rescueTarget = null;
            }
            else if (best != null && best != rescueTarget && bestScore > RescuePriority(rescueTarget, now) + 15f)
            {
                rescueTarget = best;
                rescueDeadline = now + 3f;
                monitor.SwitchRadarTargetAndSync(bestIndex);
                return;
            }
            else if (monitor.targetedPlayer == rescueTarget)
            {
                if (!ApproachShipControl(shipOperator, teleporter.buttonTrigger.transform.position))
                {
                    rescueDeadline = now + 10f;
                    return;
                }
                Plugin.Log.LogInfo($"Ship operator is teleporting endangered player {rescueTarget.playerClientId}.");
                recentTeleports[rescueTarget] = now;
                teleporter.PressTeleportButtonOnLocalClient();
                rescueTarget = null;
                nextRadarSwitch = now + 5f;
                return;
            }
            else if (rescueTarget != null)
                return;
        }
        if (best == null)
            return;
        rescueTarget = best;
        rescueDeadline = now + 3f;
        monitor.SwitchRadarTargetAndSync(bestIndex);
    }

    private static bool RecoverableBody(GameNetcodeStuff.PlayerControllerB? player)
    {
        if (!BotSettings.RecoverBodies || player == null || !player.isPlayerDead || player.deadBody == null)
            return false;
        var body = player.deadBody;
        if (body.deactivated || body.isInShip || body.parentedToShip || !body.gameObject.activeInHierarchy ||
            body.grabBodyObject != null && (body.grabBodyObject.isHeld || body.grabBodyObject.heldByPlayerOnServer || body.grabBodyObject.isInShipRoom))
            return false;
        var round = StartOfRound.Instance;
        if (round != null && round.shipBounds != null)
        {
            var bounds = round.shipBounds.bounds;
            if (bounds.Contains(body.transform.position) || body.radarDot != null && bounds.Contains(body.radarDot.position) ||
                body.grabBodyObject != null && bounds.Contains(body.grabBodyObject.transform.position))
                return false;
            if (body.bodyParts != null)
                foreach (var part in body.bodyParts)
                    if (part != null && bounds.Contains(part.position))
                        return false;
        }
        return true;
    }

    internal bool IsLate()
    {
        var clock = TimeOfDay.Instance;
        if (round.currentLevel == null || !round.currentLevel.planetHasTime || clock == null)
            return false;
        float normalized = clock.totalTime > 0f ? clock.currentDayTime / clock.totalTime : clock.normalizedTimeOfDay;
        return DirectorClock.IsExtractionTime(normalized, clock.numberOfHours, crewParticipants.Count, crewDeaths.Count,
            BotSettings.ReturnHour, BotSettings.ReturnEarlierAfterDeaths);
    }

    private bool CanCalmSlime(IBot bot, Blackboard.Contact contact, float now)
    {
        if (!(contact.Source is BlobAI) || !BotBody.TryGet(bot.Body, out var body))
            return false;
        var box = body.FindItem<BoomboxItem>();
        return box != null && box.isPlayingMusic && box.insertedBattery != null && !box.insertedBattery.empty &&
            Board.SlimeNear(bot.Position, bot.Inside, now);
    }

    private void AllocateLate(float now)
    {
        lootOwners.Clear();
        foreach (var bot in available)
        {
            var order = bot.Order;
            if (!assigned.Contains(bot) && order.Kind == TaskKind.Scavenge && IsAvailable(order.Item) && CollectionFits(bot, order.Item!) &&
                !order.Item!.isInFactory && (order.Item.transform.position - ship).sqrMagnitude <= LateScrapRadius * LateScrapRadius &&
                !lootOwners.ContainsKey(order.Item))
                lootOwners.Add(order.Item, bot);
        }

        foreach (var bot in available)
        {
            if (assigned.Contains(bot))
                continue;
            if (!bot.Aboard && Board.NearbyFleeing(bot.Position, bot.Inside, bot.Body, now) is Blackboard.Contact threat &&
                !CanCalmSlime(bot, threat, now) && Blackboard.MustFlee(threat, bot.Body) && ((bot.Position - threat.Sample.Position).sqrMagnitude < 81f || threat.Sample.Pursuing == bot.Body))
            {
                Assign(bot, new Order(TaskKind.Evade, Vector3.zero, bot.Inside));
                continue;
            }
            if (bot.Cargo != null)
            {
                if (TryRecoverNoisyLoot(bot, now, out var recovered))
                {
                    Assign(bot, new Order(TaskKind.Scavenge, recovered.transform.position, false, recovered));
                    continue;
                }
                if (!bot.Inside && TryNearbyCargo(bot, now, out var extra))
                    Assign(bot, new Order(TaskKind.Scavenge, extra.transform.position, false, extra));
                else
                    Assign(bot, new Order(TaskKind.Transport, ship));
                continue;
            }
            if (bot.Inside || (bot.Position - ship).sqrMagnitude > LateScrapRadius * LateScrapRadius)
            {
                Assign(bot, new Order(TaskKind.Return, ship));
                continue;
            }

            GrabbableObject? best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var item in scrap)
            {
                if (!IsAvailable(item) || item.isInFactory || reserved.Contains(item) ||
                    !BotBody.TryGet(bot.Body, out var lateCarrier) || !lateCarrier.CanCarryAfterPocket(item) ||
                    lootOwners.TryGetValue(item, out var owner) && owner != bot)
                    continue;
                Vector3 position = item.transform.position;
                float distance = Vector3.Distance(position, bot.Position);
                float score = LootPolicy.Score(item.scrapValue, item.itemProperties.weight,
                    distance + Vector3.Distance(position, ship), 1f, item.itemProperties.twoHanded, lateCarrier.FreeScrapSlots(), quotaGap, quotaUrgency);
                if ((position - ship).sqrMagnitude > LateScrapRadius * LateScrapRadius || score <= bestScore ||
                    Board.Risk(position, false, now) >= 0.38f || !bot.CanAttempt(position, false, now) ||
                    !bot.CanReach(position, false) || !CollectionFits(bot, item))
                    continue;
                best = item;
                bestScore = score;
            }
            Assign(bot, best != null ? new Order(TaskKind.Scavenge, best.transform.position, false, best) : new Order(TaskKind.Return, ship));
        }
    }

    private void GuardShip(float now)
    {
        if (!BotSettings.ShipSupport || !Board.OutdoorThreat || HumanAboard())
        {
            guardian = null;
            return;
        }

        foreach (var bot in available)
        {
            if (bot.Aboard && bot.CanGuard && RoleOf(bot) == BotRole.ShipSupport)
            {
                guardian = bot;
                break;
            }
        }

        if (guardian == null || !guardian.Available || !guardian.CanGuard || !guardian.Aboard)
        {
            IBot? replacement = null;
            float best = float.PositiveInfinity;
            foreach (var bot in available)
            {
                if (!bot.CanGuard)
                    continue;
                float score = bot.Aboard ? -1000f : Vector3.Distance(bot.Position, ship) + (bot.Inside ? 100f : 0f) + 150f * Board.Risk(bot.Position, bot.Inside, now);
                if (bot == guardian)
                    score -= 8f;
                if (score >= best)
                    continue;
                replacement = bot;
                best = score;
            }
            guardian = replacement;
        }

        if (guardian != null)
        {
            if (guardian.Aboard && guardian is CrewBot helper && guardian.Order.Kind == TaskKind.AssistPeeper && helper.HasPeeperTarget)
            {
                assigned.Add(guardian);
                return;
            }
            if (guardian.Aboard && guardian.Order.Kind == TaskKind.EquipWeapon && guardian.Order.Item is Shovel && (PeeperInShip() || TulipInShip()))
            {
                assigned.Add(guardian);
                return;
            }
            if (guardian.Aboard && guardian.Order.Kind == TaskKind.ShipControl && now < shipControlUntil)
            {
                assigned.Add(guardian);
                return;
            }
            if (!guardian.Aboard && Board.NearbyFleeing(guardian.Position, guardian.Inside, guardian.Body, now) is Blackboard.Contact contact &&
                Blackboard.MustFlee(contact, guardian.Body) && ((guardian.Position - contact.Sample.Position).sqrMagnitude < 81f || contact.Sample.Pursuing == guardian.Body))
                Assign(guardian, new Order(TaskKind.Evade, Vector3.zero, guardian.Inside));
            else
                Assign(guardian, new Order(TaskKind.Guard, ship));
        }
    }

    private bool PeeperInShip()
    {
        foreach (var player in round.allPlayerScripts)
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && player.isInHangarShipRoom && PeeperCompatibility.HasAttached(player))
                return true;
        return false;
    }

    private bool TulipInShip()
    {
        foreach (var player in round.allPlayerScripts)
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && player.isInHangarShipRoom && Blackboard.AttachedTulip(player) != null)
                return true;
        return false;
    }

    private void Allocate(IBot bot, IBot? partner, float now)
    {
        var nearby = Board.Nearby(bot.Position, bot.Inside, now);
        if (Board.Coordinated && bot.Inside && partner == null)
        {
            IBot? activeTeammate = null;
            float nearest = float.PositiveInfinity;
            foreach (var teammate in available)
            {
                if (teammate == bot || !teammate.Available || !teammate.Inside ||
                    teammate.Order.Kind != TaskKind.Explore && teammate.Order.Kind != TaskKind.Scavenge &&
                    teammate.Order.Kind != TaskKind.Transport && teammate.Order.Kind != TaskKind.Stage)
                    continue;
                float distance = (teammate.Position - bot.Position).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    activeTeammate = teammate;
                }
            }
            if (activeTeammate != null)
            {
                Assign(bot, new Order(TaskKind.Escort, Vector3.zero, true, partner: activeTeammate));
                return;
            }
        }
        bool immediateThreat = nearby != null && Blackboard.MustFlee(nearby, bot.Body) &&
            ((nearby.Sample.Position - bot.Position).sqrMagnitude < 64f || nearby.Sample.Pursuing == bot.Body);
        bool canExplore = ExplorationPolicy.MayExplore(Board.Coordinated, partner != null, bot.Inside,
            immediateThreat, Board.Risk(bot.Position, bot.Inside, now, false));
        var current = bot.Order;
        BotRole role = RoleOf(bot);
        if (role == BotRole.ShipSupport && BotSettings.ShipSupport)
        {
            Assign(bot, new Order(bot.Aboard ? TaskKind.Guard : TaskKind.Return, ship));
            return;
        }
        if (role == BotRole.ShipSupport)
            role = BotRole.Hauler;
        if (bot.Aboard && bot.Cargo == null && bot.Body.ItemOnlySlot == null && Board.IndoorEnemyKnown(now) &&
            BotBody.TryGet(bot.Body, out var utilityCarrier))
        {
            GrabbableObject? utility = null;
            foreach (var item in Blackboard.Items)
            {
                if (!(item is Shovel) || item.itemProperties == null ||
                    !InventoryPolicy.FitsUtilitySlot(item.itemProperties.isScrap, item.itemProperties.twoHanded,
                        item.itemProperties.disallowUtilitySlot) ||
                    !item.isInShipRoom || !item.isActiveAndEnabled || !item.IsSpawned || !item.grabbable || item.isHeld ||
                    item.heldByPlayerOnServer || item.isHeldByEnemy || reserved.Contains(item) || !CanEquip(item, now) ||
                    LightningHazard.IsTarget(item) || !utilityCarrier.CanCarryAfterPocket(item) ||
                    !bot.CanAttempt(item.transform.position, false, now) || !bot.CanReach(item.transform.position, false))
                    continue;
                if (utility == null)
                    utility = item;
            }
            if (utility != null)
            {
                Assign(bot, new Order(TaskKind.EquipWeapon, utility.transform.position, false, utility));
                return;
            }
        }
        if (bot.Aboard && bot.Cargo == null && Board.SlimeKnown(now) &&
            BotBody.TryGet(bot.Body, out var equipmentCarrier) && equipmentCarrier.FindItem<BoomboxItem>() == null)
        {
            bool assignedBox = false;
            foreach (var teammate in bots)
                if (teammate != bot && teammate.Available && BotBody.TryGet(teammate.Body, out var teammateBody) &&
                    teammateBody.FindItem<BoomboxItem>() != null)
                {
                    assignedBox = true;
                    break;
                }
            if (!assignedBox)
                foreach (var item in Blackboard.Items)
                    if (item is BoomboxItem && item.isInShipRoom && item.isActiveAndEnabled && item.IsSpawned &&
                        item.grabbable && !item.isHeld && !item.heldByPlayerOnServer && !item.isHeldByEnemy && CanEquip(item, now) &&
                        !reserved.Contains(item) && !LightningHazard.IsTarget(item) && equipmentCarrier.CanCarry(item) &&
                        bot.CanReach(item.transform.position, false))
                    {
                        AssignTeam(bot, new Order(TaskKind.EquipWeapon, item.transform.position, false, item, partner), partner);
                        return;
                    }
        }
        if (bot.Aboard && bot.Cargo == null && BotBody.TryGet(bot.Body, out var toolCarrier) &&
            toolCarrier.FindItem<LockPicker>() == null)
        {
            foreach (var item in Blackboard.Items)
                if (item is LockPicker picker && !picker.isOnDoor && item.isInShipRoom && item.isActiveAndEnabled &&
                    item.IsSpawned && item.grabbable && !item.isHeld && !item.heldByPlayerOnServer && !item.isHeldByEnemy && CanEquip(item, now) &&
                    !reserved.Contains(item) && !LightningHazard.IsTarget(item) && toolCarrier.CanCarry(item) &&
                    bot.CanReach(item.transform.position, false))
                {
                    AssignTeam(bot, new Order(TaskKind.EquipWeapon, item.transform.position, false, item, partner), partner);
                    return;
                }
        }
        if (bot.Aboard && bot.Cargo == null && BotBody.TryGet(bot.Body, out var grenadeCarrier) &&
            grenadeCarrier.FindItem<StunGrenadeItem>() == null)
        {
            foreach (var item in Blackboard.Items)
                if (item is StunGrenadeItem grenade && !grenade.pinPulled && !grenade.hasExploded &&
                    !grenade.spawnDamagingShockwave && !grenade.explodeOnCollision && !grenade.dontRequirePullingPin &&
                    grenade.chanceToExplode >= 100f && item.isInShipRoom && item.isActiveAndEnabled && item.IsSpawned &&
                    item.grabbable && !item.isHeld && !item.heldByPlayerOnServer && !item.isHeldByEnemy && CanEquip(item, now) &&
                    !reserved.Contains(item) && !LightningHazard.IsTarget(item) && grenadeCarrier.CanCarry(item) &&
                    bot.CanReach(item.transform.position, false))
                {
                    AssignTeam(bot, new Order(TaskKind.EquipWeapon, item.transform.position, false, item, partner), partner);
                    return;
                }
        }
        if (bot.Aboard && bot.Cargo == null && BotBody.TryGet(bot.Body, out var supplyCarrier))
        {
            var gun = supplyCarrier.FindItem<ShotgunItem>();
            var bag = supplyCarrier.FindItem<BeltBagItem>();
            foreach (var item in Blackboard.Items)
            {
                bool useful = item is ShotgunItem && gun == null || item is BeltBagItem && bag == null ||
                    item is GunAmmo ammo && gun != null && ammo.ammoType == gun.gunCompatibleAmmoID &&
                    supplyCarrier.FindAmmo(gun.gunCompatibleAmmoID) == null &&
                    (bag == null || bag.objectsInBag.Count < 4);
                if (!useful || !item.isInShipRoom || !item.isActiveAndEnabled || !item.IsSpawned || !item.grabbable ||
                    item.isHeld || item.heldByPlayerOnServer || item.isHeldByEnemy || reserved.Contains(item) || !CanEquip(item, now) ||
                    LightningHazard.IsTarget(item) || !bot.CanAttempt(item.transform.position, false, now) ||
                    !(item is GunAmmo && bag != null ? bag.objectsInBag.Count < 15 : supplyCarrier.CanCarryAfterPocket(item)) ||
                    !bot.CanReach(item.transform.position, false))
                    continue;
                AssignTeam(bot, new Order(TaskKind.EquipWeapon, item.transform.position, false, item, partner), partner);
                return;
            }
        }
        if (bot.Aboard && bot.Body.currentlyHeldObjectServer == null && Board.IndoorEnemyKnown(now))
        {
            foreach (var weapon in Blackboard.Items)
            {
                if (weapon is Shovel && weapon.isInShipRoom && weapon.isActiveAndEnabled && weapon.IsSpawned &&
                    weapon.grabbable && CanEquip(weapon, now) && !LightningHazard.IsTarget(weapon) && !weapon.isHeld && !weapon.heldByPlayerOnServer && !weapon.isHeldByEnemy &&
                    !reserved.Contains(weapon) && bot.CanAttempt(weapon.transform.position, false, now) &&
                    bot.CanReach(weapon.transform.position, false))
                {
                    AssignTeam(bot, new Order(TaskKind.EquipWeapon, weapon.transform.position, false, weapon, partner), partner);
                    return;
                }
            }
        }
        if (!bot.Inside && !bot.Aboard && bot.Cargo == null &&
            (current.Kind == TaskKind.Return || current.Kind == TaskKind.Transport) &&
            TryNearbyCargo(bot, now, out var returnLoot))
        {
            Assign(bot, new Order(TaskKind.Scavenge, returnLoot.transform.position, false, returnLoot));
            return;
        }
        if (!bot.Aboard && bot.Cargo == null && TryHumanDroppedCargo(bot, now, out var humanItem))
        {
            AssignTeam(bot, new Order(TaskKind.Scavenge, humanItem.transform.position, bot.Inside, humanItem, partner), partner);
            return;
        }
        if (!bot.Inside && bot.Cargo == null && TryEntranceBatch(bot, now, out var batchItem))
        {
            Assign(bot, new Order(TaskKind.Scavenge, batchItem.transform.position, false, batchItem));
            return;
        }
        if (!bot.Inside && !bot.Aboard && bot.Cargo == null && bot.CanEnterFacility(now) &&
            TryNearbyEntrance(bot, now, out var nearbyEntranceTarget))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, nearbyEntranceTarget, true, partner: partner), partner);
            return;
        }
        bool enteringFacility = current.Kind == TaskKind.Explore && current.Inside && !bot.Inside;
        if ((enteringFacility || now - bot.AssignedAt < Commitment) && current.Partner == partner &&
            (!enteringFacility || DestinationEntranceAllowed(current.Point, now)) && bot.CanAttempt(current.Point, current.Inside, now) &&
            !(current.Kind == TaskKind.Explore && current.Inside == bot.Inside && (current.Point - bot.Position).sqrMagnitude < 9f) &&
            (current.Kind == TaskKind.Explore && canExplore || current.Kind == TaskKind.Scavenge && IsAvailable(current.Item) && CanTakeEntranceLoot(current.Item!) && (current.Item!.isInFactory || CollectionFits(bot, current.Item)) && !reserved.Contains(current.Item!) &&
                (!lootOwners.TryGetValue(current.Item!, out var owner) || owner == bot)) &&
            Board.Risk(current.Point, current.Inside, now, current.Kind != TaskKind.Explore) < 0.48f)
        {
            AssignTeam(bot, current, partner);
            return;
        }

        if (bot.Aboard && bot.Cargo == null && BotBody.TryGet(bot.Body, out var carrier))
        {
            GrabbableObject? nearbyItem = null;
            float nearbyScore = float.NegativeInfinity;
            foreach (var item in scrap)
            {
                if (!IsAvailable(item) || !CanTakeEntranceLoot(item) || item.isInFactory || reserved.Contains(item) ||
                    lootOwners.TryGetValue(item, out var nearbyOwner) && nearbyOwner != bot || !carrier.CanCarry(item))
                    continue;
                float distance = (item.transform.position - ship).sqrMagnitude;
                float score = LootPolicy.Score(item.scrapValue, item.itemProperties.weight, Mathf.Sqrt(distance), TimePressure(),
                    item.itemProperties.twoHanded, carrier.FreeScrapSlots(), quotaGap, quotaUrgency);
                if (distance >= 144f || score <= nearbyScore || Board.Risk(item.transform.position, false, now) >= 0.3f ||
                    !bot.CanAttempt(item.transform.position, false, now) || !bot.CanReach(item.transform.position, false) || !CollectionFits(bot, item))
                    continue;
                nearbyScore = score;
                nearbyItem = item;
            }
            if (nearbyItem != null)
            {
                AssignTeam(bot, new Order(TaskKind.Scavenge, nearbyItem.transform.position, false, nearbyItem, partner), partner);
                return;
            }
        }

        bool seekingEntrance = canExplore && !bot.Inside;
        if (seekingEntrance && bot.CanEnterFacility(now) && TryHiddenFacilityEntry(bot, now))
        {
            assigned.Add(bot);
            return;
        }
        if (seekingEntrance && bot.CanEnterFacility(now) && TryRandomEntrance(bot, now, out var entranceTarget))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, entranceTarget, true, partner: partner), partner);
            return;
        }

        GrabbableObject? bestItem = null;
        float bestScore = role == BotRole.Hauler ? -0.1f : 0.1f;
        float pressure = TimePressure();
        foreach (var item in scrap)
        {
            if (!BotBody.TryGet(bot.Body, out var lootCarrier) || !lootCarrier.CanCarryAfterPocket(item))
                continue;
            if (!IsAvailable(item) || !CanTakeEntranceLoot(item) || reserved.Contains(item))
                continue;
            if (!item.isInFactory && !CollectionFits(bot, item))
                continue;
            if (lootOwners.TryGetValue(item, out var itemOwner) && itemOwner != bot)
                continue;
            if (staged.TryGetValue(item, out var staging) && staging.Bot == bot && now - staging.Time < 45f &&
                !IsEntranceBatchItem(item))
                continue;

            Vector3 position = Board.KnownLootPosition(item);
            if (!bot.CanAttempt(position, item.isInFactory, now))
                continue;
            if (Board.Coordinated && partner == null && !canExplore && (item.isInFactory || Board.Risk(position, false, now) > 0.18f))
                continue;

            float risk = Board.Risk(position, item.isInFactory, now);
            bool bodyRecovery = item is RagdollGrabbableObject;
            if (risk >= (bodyRecovery ? 0.22f : 0.48f) || bodyRecovery && bot.Body.health < 50)
                continue;

            float distance = Vector3.Distance(position, bot.Position) + (item.isInFactory == bot.Inside ? 0f : 24f);
            if (item.isInFactory && distance > (bodyRecovery ? 32f : 48f))
                continue;
            if (item.isInFactory && !bot.Inside || item.isInFactory == bot.Inside && !bot.CanReach(position, bot.Inside))
                continue;

            float deliveryDistance = item.isInFactory ? IndoorDeliveryDistance(position) : Vector3.Distance(position, ship);
            float score = LootPolicy.Score(bodyRecovery ? 80 : item.scrapValue, item.itemProperties.weight,
                distance + deliveryDistance * 0.35f,
                pressure, item.itemProperties.twoHanded, lootCarrier.FreeScrapSlots(), bodyRecovery ? 0f : quotaGap, quotaUrgency) - risk * (bodyRecovery ? 5f : 2f) + learning.Estimate("task:Scavenge") * 0.5f +
                learning.Estimate("task:Transport") * 0.35f + learning.Estimate(item.isInFactory ? "route:inside" : "route:outside") * 0.4f;
            if (!item.isInFactory)
                score += 1f;
            if (score <= bestScore)
                continue;
            bestScore = score;
            bestItem = item;
        }

        float explorationScrapThreshold = role == BotRole.Explorer ? 0.65f : 0.35f;
        if (bestItem != null && (!canExplore || role == BotRole.Hauler || bot.Inside && bestItem.isInFactory &&
            (bestItem.transform.position - bot.Position).sqrMagnitude < 144f ||
            bestScore >= explorationScrapThreshold + learning.Estimate("task:Explore") * 0.4f))
        {
            AssignTeam(bot, new Order(TaskKind.Scavenge, Board.KnownLootPosition(bestItem), bestItem.isInFactory, bestItem, partner), partner);
            return;
        }

        if (seekingEntrance && bot.CanEnterFacility(now) && TryApproachEntrance(bot, now, out var entranceApproach))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, entranceApproach, false, partner: partner), partner);
            return;
        }

        if (canExplore && !immediateThreat && bot.Inside && bot.Cargo == null && TryMineshaftDescent(bot, partner, now, out var lowerTarget))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, lowerTarget, true, partner: partner), partner);
            return;
        }

        if (canExplore && !seekingEntrance && TryExplore(bot, now, out var point, out bool inside))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, point, inside, partner: partner), partner);
            return;
        }

        if (bestItem != null)
        {
            AssignTeam(bot, new Order(TaskKind.Scavenge, Board.KnownLootPosition(bestItem), bestItem.isInFactory, bestItem, partner), partner);
            return;
        }

        if (canExplore && !seekingEntrance && TryRandomWander(bot, now, out var randomPoint))
        {
            AssignTeam(bot, new Order(TaskKind.Explore, randomPoint, bot.Inside, partner: partner), partner);
            return;
        }

        Assign(bot, new Order(TaskKind.Return, ship));
    }

    private void AssignBabyCare(float now)
    {
        foreach (var enemy in Blackboard.Enemies)
        {
            if (!(enemy is CaveDwellerAI baby) || baby.isEnemyDead || !baby.isActiveAndEnabled ||
                baby.currentBehaviourStateIndex != 0 || baby.propScript == null || !baby.propScript.IsSpawned)
                continue;
            var prop = baby.propScript;
            IBot? selected = null;
            float nearest = float.PositiveInfinity;
            foreach (var bot in available)
            {
                if (assigned.Contains(bot) || bot.Inside == baby.isOutside || bot.Aboard ||
                    !BotBody.TryGet(bot.Body, out var carrier))
                    continue;
                bool holding = prop.playerHeldBy == bot.Body && prop.isHeld;
                if (!holding && (!baby.babyCrying || prop.isHeld || prop.heldByPlayerOnServer || !prop.grabbable ||
                    bot.Cargo != null || !carrier.CanCarryAfterPocket(prop) ||
                    !bot.CanAttempt(prop.transform.position, bot.Inside, now) || !bot.CanReach(prop.transform.position, bot.Inside)))
                    continue;
                float distance = (bot.Position - prop.transform.position).sqrMagnitude;
                if (!holding && (distance > 400f || Board.Risk(prop.transform.position, bot.Inside, now, false) >= 0.35f))
                    continue;
                if (holding || distance < nearest)
                {
                    selected = bot;
                    nearest = distance;
                    if (holding)
                        break;
                }
            }
            if (selected != null)
                Assign(selected, new Order(TaskKind.CareBaby, prop.transform.position, selected.Inside, prop));
        }
    }

    private void RefreshRoles()
    {
        staleRoles.Clear();
        foreach (var pair in roles)
            if (!available.Contains(pair.Key))
                staleRoles.Add(pair.Key);
        foreach (var bot in staleRoles)
            roles.Remove(bot);

        BotRolePolicy.Targets(available.Count, out int targetExplorers, out int targetHaulers, out int targetSupport);
        if (targetSupport > 0 && BotSettings.ShipSupport)
        {
            IBot? preferred = null;
            foreach (var bot in available)
            {
                bool current = roles.TryGetValue(bot, out var existing) && existing == BotRole.ShipSupport;
                bool previousCurrent = preferred != null && roles.TryGetValue(preferred, out var previous) && previous == BotRole.ShipSupport;
                if (preferred == null || BotRolePolicy.PreferSupport(bot.Body.health, current, bot.Aboard,
                    preferred.Body.health, previousCurrent, preferred.Aboard))
                    preferred = bot;
            }
            foreach (var bot in available)
                if (bot != preferred && roles.TryGetValue(bot, out var existing) && existing == BotRole.ShipSupport)
                    roles.Remove(bot);
            if (preferred != null)
                roles[preferred] = BotRole.ShipSupport;
        }
        int explorerCount = 0;
        int haulerCount = 0;
        int supportCount = 0;
        roleCandidates.Clear();
        foreach (var bot in available)
        {
            if (!roles.TryGetValue(bot, out var role))
            {
                roleCandidates.Add(bot);
                continue;
            }

            bool overTarget = role == BotRole.Explorer ? explorerCount >= targetExplorers :
                role == BotRole.Hauler ? haulerCount >= targetHaulers : supportCount >= targetSupport;
            if (overTarget)
            {
                roles.Remove(bot);
                roleCandidates.Add(bot);
                continue;
            }

            if (role == BotRole.Explorer)
                explorerCount++;
            else if (role == BotRole.Hauler)
                haulerCount++;
            else
                supportCount++;
        }

        while (roleCandidates.Count > 0)
        {
            BotRole role;
            if (supportCount < targetSupport)
                role = BotRole.ShipSupport;
            else if (haulerCount < targetHaulers)
                role = BotRole.Hauler;
            else
                role = BotRole.Explorer;

            int selectedIndex = 0;
            float bestScore = float.NegativeInfinity;
            for (int index = 0; index < roleCandidates.Count; index++)
            {
                var candidate = roleCandidates[index];
                float score = role == BotRole.ShipSupport
                    ? (candidate.Aboard ? 1000f : 0f) - Vector3.Distance(candidate.Position, ship)
                    : role == BotRole.Hauler
                        ? (candidate.Cargo != null ? 1000f : 0f) + (candidate.Order.Kind == TaskKind.Transport ? 100f : 0f)
                        : (candidate.Inside ? 10f : 0f);
                if (score > bestScore)
                {
                    selectedIndex = index;
                    bestScore = score;
                }
            }

            var selected = roleCandidates[selectedIndex];
            roleCandidates.RemoveAt(selectedIndex);
            roles[selected] = role;
            if (role == BotRole.Explorer)
                explorerCount++;
            else if (role == BotRole.Hauler)
                haulerCount++;
            else
                supportCount++;
            history.Record(selected, "role_assigned", $"role={role}");
        }
    }

    private BotRole RoleOf(IBot bot)
    {
        return roles.TryGetValue(bot, out var role) ? role : BotRole.Explorer;
    }

    internal bool IsShipOperator(IBot bot)
    {
        return bot == guardian || bot == doorOperator || bot == departureBot ||
            bot.Order.Kind == TaskKind.Guard || bot.Order.Kind == TaskKind.ShipControl ||
            BotSettings.ShipSupport && RoleOf(bot) == BotRole.ShipSupport;
    }

    private bool TryExplore(IBot bot, float now, out Vector3 point, out bool inside)
    {
        inside = bot.Inside;
        var nodes = inside ? insideNodes : outsideNodes;
        float best = float.NegativeInfinity;
        point = default;
        foreach (var node in nodes)
        {
            float distance = (node - bot.Position).sqrMagnitude;
            if (distance < (inside ? 2.25f : 16f) || !inside && distance > 55f * 55f || Board.Risk(node, inside, now, false) >= 0.38f)
                continue;
            var cell = Vector3Int.FloorToInt(node / (inside ? 3f : 8f));
            if (!bot.CanAttempt(node, inside, now))
                continue;
            if (inside && WasVisited(node) || !inside && visits.TryGetValue(cell, out float visited) && now - visited < 70f)
                continue;

            bool claimed = false;
            foreach (var owner in explorationOwners)
            {
                if (owner != bot && owner != bot.Order.Partner && owner.Order.Kind == TaskKind.Explore && owner.Order.Inside == inside &&
                    (owner.Order.Point - node).sqrMagnitude < 100f)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed)
                continue;
            foreach (var other in assigned)
            {
                if (other != bot && other.Order.Kind == TaskKind.Explore && other.Order.Inside == inside && (other.Order.Point - node).sqrMagnitude < 100f)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed)
                continue;
            float score = -Mathf.Abs(Mathf.Sqrt(distance) - 15f) - 20f * Board.Risk(node, inside, now, false);
            if (!inside)
                score += OutdoorRouteEstimate(bot.Position, node) * 6f;
            if (score > best && bot.CanReach(node, inside))
            {
                best = score;
                point = node;
            }
        }

        return !float.IsNegativeInfinity(best);
    }

    private bool TryMineshaftDescent(IBot bot, IBot? partner, float now, out Vector3 point)
    {
        point = default;
        var lift = Elevator;
        if (lift == null || lift.elevatorTopPoint == null || lift.elevatorBottomPoint == null ||
            (bot.Position - lift.elevatorTopPoint.position).sqrMagnitude > 400f ||
            !DifferentElevatorFloors(bot.Position, lift.elevatorBottomPoint.position))
            return false;

        Vector3 landing = lift.elevatorBottomPointDoor != null ? lift.elevatorBottomPointDoor.position : lift.elevatorBottomPoint.position;
        float bestScore = float.PositiveInfinity;
        bool hasLowerNode = false;
        foreach (var node in insideNodes)
        {
            if (!DifferentElevatorFloors(bot.Position, node) || Mathf.Abs(node.y - landing.y) > 8f)
                continue;
            if ((node - landing).sqrMagnitude > bestScore + 16f)
                continue;
            if (!NavMesh.SamplePosition(node, out var nodeHit, 1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(nodeHit.position.y - node.y) > 1.5f || !DifferentElevatorFloors(bot.Position, nodeHit.position))
                continue;
            hasLowerNode = true;
            float distance = (nodeHit.position - landing).sqrMagnitude;
            if (distance >= bestScore || WasVisited(nodeHit.position) || !bot.CanAttempt(nodeHit.position, true, now) ||
                Board.Risk(nodeHit.position, true, now, false) >= 0.38f)
                continue;
            bool claimed = false;
            foreach (var other in explorationOwners)
                if (other != bot && other != partner && other.Order.Kind == TaskKind.Explore && other.Order.Inside &&
                    (other.Order.Point - nodeHit.position).sqrMagnitude < 25f)
                {
                    claimed = true;
                    break;
                }
            if (claimed)
                continue;
            foreach (var other in assigned)
                if (other != bot && other != partner && other.Order.Kind == TaskKind.Explore && other.Order.Inside &&
                    (other.Order.Point - nodeHit.position).sqrMagnitude < 25f)
                {
                    claimed = true;
                    break;
                }
            if (claimed)
                continue;
            bestScore = distance;
            point = nodeHit.position;
        }
        if (!float.IsPositiveInfinity(bestScore))
            return true;
        if (hasLowerNode || !NavMesh.SamplePosition(landing, out var hit, 4f, NavMesh.AllAreas) ||
            !DifferentElevatorFloors(bot.Position, hit.position) || !bot.CanAttempt(hit.position, true, now) ||
            Board.Risk(hit.position, true, now, false) >= 0.38f)
            return false;
        point = hit.position;
        return true;
    }

    private bool TryRandomWander(IBot bot, float now, out Vector3 point)
    {
        point = default;
        int attempt = 0;
        for (; attempt < 32; ++attempt)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * 25f;
            Vector3 candidate = bot.Position + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - bot.Position.y) > 2.5f || (hit.position - bot.Position).sqrMagnitude < 9f ||
                Board.Risk(hit.position, bot.Inside, now, false) >= 0.38f || !bot.CanAttempt(hit.position, bot.Inside, now) ||
                bot.Inside && WasVisited(hit.position))
                continue;
            bool claimed = false;
            foreach (var other in explorationOwners)
            {
                if (other != bot && other != bot.Order.Partner && other.Order.Kind == TaskKind.Explore && other.Order.Inside == bot.Inside &&
                    (other.Order.Point - hit.position).sqrMagnitude < 25f)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed)
                continue;
            foreach (var other in assigned)
            {
                if (other != bot && other.Order.Kind == TaskKind.Explore && other.Order.Inside == bot.Inside &&
                    (other.Order.Point - hit.position).sqrMagnitude < 25f)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed || !bot.CanReach(hit.position, bot.Inside))
                continue;
            point = hit.position;
            return true;
        }
        return false;
    }

    private bool TryHiddenFacilityEntry(IBot bot, float now)
    {
        if (!(bot is CrewBot crew) || now < nextHiddenEntryCheck || now - directorStartedAt < 20f || bot.Inside || bot.Cargo != null ||
            bot.ElevatorTransit || bot.Body.inAnimationWithEnemy != null || Board.ActiveJester != null ||
            Board.Risk(bot.Position, false, now, false) >= 0.35f ||
            hiddenEntryRetry.TryGetValue(bot, out float retry) && now < retry)
            return false;
        hiddenEntryRetry[bot] = now + 5f;
        nextHiddenEntryCheck = now + 0.75f;
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entrancePoint == null)
                continue;
            foreach (var candidate in available)
                if (!candidate.Inside && candidate.Available && candidate.TryPortalApproach(entrance.entrancePoint.position, out var approach) &&
                    IsPortalApproach(approach, entrance.entrancePoint.position))
                    return false;
        }
        if (insideNodes.Count == 0 || !hiddenFromHumanViews(bot.Position))
            return false;
        int start = UnityEngine.Random.Range(0, insideNodes.Count);
        int attempts = Mathf.Min(insideNodes.Count, 24);
        for (int index = 0; index < attempts; index++)
        {
            Vector3 node = insideNodes[(start + index) % insideNodes.Count];
            if (!NavMesh.SamplePosition(node, out var hit, 1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - node.y) > 1.5f || Board.Risk(hit.position, true, now, false) >= 0.25f ||
                Board.Spikes.At(hit.position) || !hiddenFromHumanViews(hit.position) || !hiddenFromHumanViews(bot.Position))
                continue;
            if (crew.HiddenEnter(hit.position, now))
            {
                history.Record(bot, "hidden_entry", $"destination={hit.position} reason=no_reachable_entrance");
                return true;
            }
        }
        return false;
    }

    private bool TryApproachEntrance(IBot bot, float now, out Vector3 point)
    {
        point = default;
        float best = float.PositiveInfinity;
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entrancePoint == null ||
                entrance.exitScript == null || entrance.exitScript.entrancePoint == null || !EntranceAllowed(entrance, now))
                continue;
            Vector3 destination = entrance.entrancePoint.position;
            if (!bot.CanAttempt(destination, false, now) ||
                Board.Risk(destination, false, now) >= 0.35f ||
                Board.Risk(entrance.exitScript.entrancePoint.position, true, now) >= 0.35f)
                continue;
            float currentDistance = Vector3.Distance(bot.Position, destination);
            foreach (var node in outsideNodes)
            {
                float remaining = Vector3.Distance(node, destination);
                float travel = Vector3.Distance(bot.Position, node);
                if (travel < 3f || remaining + 3f >= currentDistance ||
                    !bot.CanAttempt(node, false, now) || Board.Risk(node, false, now) >= 0.38f)
                    continue;
                float score = remaining + travel * 0.25f;
                if (score >= best || !bot.CanReach(node, false))
                    continue;
                best = score;
                point = node;
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    private static bool IsNearbyEntrance(Vector3 position, Vector3 entrance)
    {
        Vector3 offset = position - entrance;
        float vertical = Mathf.Abs(offset.y);
        offset.y = 0f;
        return vertical <= 3f && offset.sqrMagnitude <= 36f;
    }

    private static bool IsPortalApproach(Vector3 position, Vector3 entrance)
    {
        Vector3 offset = position - entrance;
        float vertical = Mathf.Abs(offset.y);
        offset.y = 0f;
        return vertical <= 3f && offset.sqrMagnitude <= 12.25f;
    }

    private bool TryNearbyEntrance(IBot bot, float now, out Vector3 target)
    {
        target = default;
        EntranceTeleport? nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entrancePoint == null ||
                !IsNearbyEntrance(bot.Position, entrance.entrancePoint.position) || !EntranceAllowed(entrance, now))
                continue;
            if (entrance.exitScript == null)
            {
                foreach (var exit in entrances)
                {
                    if (exit != null && !exit.isEntranceToBuilding && exit.entranceId == entrance.entranceId)
                    {
                        entrance.exitScript = exit;
                        break;
                    }
                }
            }
            if (entrance.exitScript == null || entrance.exitScript.entrancePoint == null)
                continue;
            Vector3 entry = entrance.entrancePoint.position;
            Vector3 destination = entrance.exitScript.entrancePoint.position;
            float distance = (bot.Position - entry).sqrMagnitude;
            if (distance >= bestDistance || !bot.CanAttempt(entry, false, now) ||
                !bot.CanAttempt(destination, true, now) || Board.Risk(entry, false, now, false) >= 0.35f ||
                Board.Risk(destination, true, now, false) >= 0.35f ||
                !bot.TryPortalApproach(entry, out var approach) || !IsPortalApproach(approach, entry))
                continue;
            bestDistance = distance;
            nearest = entrance;
            target = destination;
        }
        return nearest != null;
    }

    private bool TryRandomEntrance(IBot bot, float now, out Vector3 target)
    {
        target = default;
        float totalWeight = 0f;
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entrancePoint == null ||
                !EntranceAllowed(entrance, now))
                continue;
            if (entrance.exitScript == null)
            {
                foreach (var exit in entrances)
                {
                    if (exit != null && !exit.isEntranceToBuilding && exit.entranceId == entrance.entranceId)
                    {
                        entrance.exitScript = exit;
                        break;
                    }
                }
            }
            if (entrance.exitScript == null || entrance.exitScript.entrancePoint == null)
                continue;
            Vector3 destination = entrance.exitScript.entrancePoint.position;
            if (!bot.CanAttempt(destination, true, now) || Board.Risk(entrance.entrancePoint.position, false, now) >= 0.35f ||
                Board.Risk(destination, true, now) >= 0.35f)
                continue;
            if (!bot.CanAttempt(entrance.entrancePoint.position, false, now) ||
                !bot.TryPortalApproach(entrance.entrancePoint.position, out _))
                continue;
            if (entrance.entranceId == bot.PreferredEntranceId)
            {
                target = destination;
                return true;
            }
            float weight = Mathf.Clamp(1f + learning.Estimate(EntranceKey(entrance.entranceId)) * 0.6f +
                OutdoorRouteEstimate(bot.Position, entrance.entrancePoint.position) * 0.4f, 0.2f, 2f);
            totalWeight += weight;
            if (UnityEngine.Random.value * totalWeight < weight)
                target = destination;
        }
        return totalWeight > 0f;
    }

    private bool TryOutsideEntrance(Vector3 position, out int entranceId)
    {
        entranceId = -1;
        float best = 64f;
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entrancePoint == null)
                continue;
            Vector3 offset = position - entrance.entrancePoint.position;
            if (Mathf.Abs(offset.y) > 3f)
                continue;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            if (distance >= best)
                continue;
            best = distance;
            entranceId = entrance.entranceId;
        }
        return entranceId >= 0;
    }

    internal bool NearOutsideEntrance(Vector3 position, int entranceId)
    {
        foreach (var entrance in entrances)
        {
            if (entrance == null || !entrance.isEntranceToBuilding || entrance.entranceId != entranceId || entrance.entrancePoint == null)
                continue;
            Vector3 offset = position - entrance.entrancePoint.position;
            if (Mathf.Abs(offset.y) > 3f)
                return false;
            offset.y = 0f;
            return offset.sqrMagnitude < 64f;
        }
        return false;
    }

    private void RefreshEntranceBatches()
    {
        entranceLootCounts.Clear();
        foreach (var item in scrap)
        {
            if (!IsAvailable(item) || item.isInFactory || item is RagdollGrabbableObject ||
                !TryOutsideEntrance(item.transform.position, out int id))
                continue;
            entranceLootCounts.TryGetValue(id, out int count);
            entranceLootCounts[id] = count + 1;
        }
        entranceBatches.Refresh(entranceLootCounts);
    }

    private bool IsEntranceBatchItem(GrabbableObject item)
    {
        return !item.isInFactory && !(item is RagdollGrabbableObject) && item.itemProperties != null && item.itemProperties.isScrap &&
            TryOutsideEntrance(item.transform.position, out int id) && entranceBatches.IsActive(id);
    }

    private bool CanTakeEntranceLoot(GrabbableObject item)
    {
        return Blackboard.HumanDropped(item) || item.isInFactory || item is RagdollGrabbableObject || IsLate() ||
            !TryOutsideEntrance(item.transform.position, out int id) || entranceBatches.IsActive(id);
    }

    private bool TryEntranceBatch(IBot bot, float now, out GrabbableObject result)
    {
        result = null!;
        if (!BotBody.TryGet(bot.Body, out var carrier))
            return false;
        float best = float.NegativeInfinity;
        foreach (var item in scrap)
        {
            if (!IsAvailable(item) || !IsEntranceBatchItem(item) || reserved.Contains(item) ||
                lootOwners.TryGetValue(item, out var owner) && owner != bot || !carrier.CanCarryAfterPocket(item) ||
                !bot.CanAttempt(item.transform.position, false, now))
                continue;
            Vector3 position = item.transform.position;
            float distance = Vector3.Distance(position, bot.Position);
            float score = LootPolicy.Score(item.scrapValue, item.itemProperties.weight, distance, TimePressure(),
                item.itemProperties.twoHanded, carrier.FreeScrapSlots(), quotaGap, quotaUrgency);
            if (score <= best || Board.Risk(position, false, now) >= 0.35f || !bot.CanReach(position, false) || !CollectionFits(bot, item))
                continue;
            result = item;
            best = score;
        }
        return result != null;
    }

    private bool TryHumanDroppedCargo(IBot bot, float now, out GrabbableObject result)
    {
        result = null!;
        if (!BotBody.TryGet(bot.Body, out var carrier))
            return false;
        float nearest = 400f;
        foreach (var item in scrap)
        {
            if (!Blackboard.HumanDropped(item) || !IsAvailable(item) || item.isInFactory != bot.Inside || reserved.Contains(item) ||
                lootOwners.TryGetValue(item, out var owner) && owner != bot || !carrier.CanCarryAfterPocket(item) ||
                !bot.CanAttempt(item.transform.position, bot.Inside, now))
                continue;
            float distance = (item.transform.position - bot.Position).sqrMagnitude;
            if (distance >= nearest || Board.Risk(item.transform.position, bot.Inside, now, false) >= 0.35f ||
                !bot.CanReach(item.transform.position, bot.Inside) || !item.isInFactory && !CollectionFits(bot, item))
                continue;
            nearest = distance;
            result = item;
        }
        return result != null;
    }

    private bool TryNearbyCargo(IBot bot, float now, out GrabbableObject item)
    {
        item = null!;
        if (bot.Aboard || bot.Cargo is RagdollGrabbableObject || !BotBody.TryGet(bot.Body, out var carrier))
            return false;
        float bestScore = float.NegativeInfinity;
        float pressure = TimePressure();
        float distanceToShip = Vector3.Distance(bot.Position, ship);
        foreach (var candidate in scrap)
        {
            if (!IsAvailable(candidate) || !CanTakeEntranceLoot(candidate) || candidate is RagdollGrabbableObject ||
                candidate.isInFactory != bot.Inside || reserved.Contains(candidate) ||
                lootOwners.TryGetValue(candidate, out var owner) && owner != bot ||
                !carrier.CanCarryAfterPocket(candidate) || !bot.CanAttempt(candidate.transform.position, bot.Inside, now))
                continue;
            Vector3 position = candidate.transform.position;
            float distance = (position - bot.Position).sqrMagnitude;
            float travel = Mathf.Sqrt(distance);
            if (bot.Inside ? travel > 12f :
                distance > 64f || Vector3.Distance(position, ship) > distanceToShip + 2f ||
                IsLate() && (position - ship).sqrMagnitude > LateScrapRadius * LateScrapRadius)
                continue;
            float score = LootPolicy.Score(candidate.scrapValue, candidate.itemProperties.weight, travel, pressure,
                candidate.itemProperties.twoHanded, carrier.FreeScrapSlots(), quotaGap, quotaUrgency);
            if (score <= bestScore || Mathf.Abs(position.y - bot.Position.y) > 3f ||
                Board.Risk(position, bot.Inside, now) >= 0.35f || !bot.CanReach(position, bot.Inside) ||
                !bot.Inside && !CollectionFits(bot, candidate))
                continue;
            bestScore = score;
            item = candidate;
        }
        return item != null;
    }

    internal bool CanCollectNearby(IBot bot, GrabbableObject item, float now)
    {
        if (!IsAvailable(item) || !CanTakeEntranceLoot(item) || item.isInFactory != bot.Inside ||
            reserved.Contains(item) && (!lootOwners.TryGetValue(item, out var reservedOwner) || reservedOwner != bot) ||
            staged.TryGetValue(item, out var staging) && staging.Bot == bot && now - staging.Time < 45f && !IsEntranceBatchItem(item) ||
            lootOwners.TryGetValue(item, out var owner) && owner != bot ||
            Board.Risk(item.transform.position, item.isInFactory, now) >= 0.35f ||
            !item.isInFactory && !CollectionFits(bot, item))
            return false;
        reserved.Add(item);
        lootOwners[item] = bot;
        return true;
    }

    internal bool CanTakeSharedEquipment(IBot bot, GrabbableObject item, float now)
    {
        return !equipmentHandoffs.TryGetValue(item, out var handoff) || handoff.Until <= now || handoff.Receiver == bot;
    }

    private void AssignEquipmentHandoffs(float now)
    {
        expiredHandoffs.Clear();
        foreach (var entry in equipmentHandoffs)
        {
            var item = entry.Key;
            var recipient = entry.Value.Receiver;
            if (item == null || !item.IsSpawned || item.isHeld || item.heldByPlayerOnServer || !recipient.Available ||
                entry.Value.Until <= now || (recipient.Position - item.transform.position).sqrMagnitude > 36f)
            {
                expiredHandoffs.Add(item!);
                continue;
            }
            if (!assigned.Contains(recipient) && Board.Risk(recipient.Position, recipient.Inside, now, false) < 0.35f)
                Assign(recipient, new Order(TaskKind.EquipWeapon, item.transform.position, recipient.Inside, item));
        }
        foreach (var item in expiredHandoffs)
            equipmentHandoffs.Remove(item);

        foreach (var recipient in available)
        {
            if (!recipient.Inside || assigned.Contains(recipient) || !BotBody.TryGet(recipient.Body, out var receiver) ||
                Board.Risk(recipient.Position, true, now, false) >= 0.25f)
                continue;
            bool needsKey = receiver.FindItem<KeyItem>() == null && LockedDoorNear(recipient.Position) != null;
            bool needsWeapon = receiver.FindItem<Shovel>() == null && Board.IndoorEnemyKnown(now);
            if (!needsKey && !needsWeapon)
                continue;
            foreach (var donor in available)
            {
                if (donor == recipient || !donor.Inside || donor.ElevatorTransit ||
                    (donor.Position - recipient.Position).sqrMagnitude > 4f ||
                    !BotBody.TryGet(donor.Body, out var donorBody) || Board.Risk(donor.Position, true, now, false) >= 0.25f)
                    continue;
                GrabbableObject? item = needsKey ? donorBody.FindItem<KeyItem>() : null;
                if (item == null && needsWeapon)
                {
                    int count = donor.Body.ItemOnlySlot is Shovel ? 1 : 0;
                    foreach (var held in donor.Body.ItemSlots)
                        if (held is Shovel)
                            count++;
                    if (count > 1)
                        item = donorBody.FindItem<Shovel>();
                }
                if (item == null || LightningHazard.IsTarget(item) || !receiver.CanCarryAfterPocket(item) || !donorBody.DropItem(item))
                    continue;
                equipmentHandoffs[item] = (recipient, now + 10f);
                Assign(recipient, new Order(TaskKind.EquipWeapon, item.transform.position, true, item));
                history.Record(donor, "equipment_handoff", $"item={item.name} recipient={recipient.Body.playerUsername}");
                break;
            }
        }
    }

    private float IndoorDeliveryDistance(Vector3 position)
    {
        float best = float.PositiveInfinity;
        foreach (var entrance in entrances)
        {
            if (entrance == null || entrance.isEntranceToBuilding || entrance.entrancePoint == null ||
                entrance.exitScript == null || entrance.exitScript.entrancePoint == null)
                continue;
            float distance = Vector3.Distance(position, entrance.entrancePoint.position) +
                Vector3.Distance(entrance.exitScript.entrancePoint.position, ship);
            if (distance < best)
                best = distance;
        }
        return float.IsPositiveInfinity(best) ? 24f : best;
    }

    private bool IsAvailable(GrabbableObject? item)
    {
        return item != null && NoisyLootReady(item, Time.time) && !Blackboard.GuardedBeeHive(item) && !Blackboard.HoarderNearItem(item) && item.isActiveAndEnabled && item.IsSpawned && item.itemProperties != null && (item is RagdollGrabbableObject body && body.ragdoll != null && BotSettings.RecoverBodies ||
            item.itemProperties.isScrap && item.scrapValue > 0 && BotSettings.CollectItems) && item.grabbable && !LightningHazard.IsTarget(item) && !item.isHeld && !item.heldByPlayerOnServer && !item.isHeldByEnemy && !item.isInShipRoom;
    }

    private void AssignTeam(IBot leader, Order order, IBot? partner)
    {
        Assign(leader, order);
        if (Board.Coordinated && partner != null && leader.Inside && partner.Inside)
            Assign(partner, new Order(TaskKind.Escort, Vector3.zero, true, partner: leader));
    }

    private void Assign(IBot bot, Order order)
    {
        var previousItem = bot.Order.Item;
        if (previousItem != null && previousItem != order.Item && lootOwners.TryGetValue(previousItem, out var owner) && owner == bot)
            lootOwners.Remove(previousItem);
        assigned.Add(bot);
        if (order.Item != null)
        {
            reserved.Add(order.Item);
            lootOwners[order.Item] = bot;
        }
        if (bot.Order.Kind != order.Kind || bot.Order.Item != order.Item || bot.Order.Inside != order.Inside || (bot.Order.Point - order.Point).sqrMagnitude >= 1f)
            history.Record(bot, "assignment", $"next={order.Kind} target={order.Point} item={(order.Item != null ? order.Item.name : "none")}");
        bot.Assign(order);
    }

    internal void Dispose()
    {
        foreach (var link in jumpLinks)
            if (link.valid)
                link.Remove();
        foreach (var pair in doorObstacles)
        {
            if (pair.Key != null && pair.Value.Obstacle != null && !pair.Key.isLocked)
                pair.Value.Obstacle.carving = pair.Value.Carving;
        }
        if (shipMesh.valid)
            shipMesh.Remove();
        if (shipMeshData != null)
            UnityEngine.Object.Destroy(shipMeshData);
        learning.Save();
    }
}
