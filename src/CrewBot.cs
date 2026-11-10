using System;
using UnityEngine;
using UnityEngine.AI;
using GameNetcodeStuff;
using Unity.Netcode;
using HarmonyLib;

namespace LethalFakeClient;

internal sealed class CrewBot : IDisposable, IBot
{
    private static readonly Action<GrabbableObject, bool, bool> ActivateItemRpc =
        AccessTools.MethodDelegate<Action<GrabbableObject, bool, bool>>(AccessTools.Method(typeof(GrabbableObject), "ActivateItemRpc"));
    private enum LiftPhase { None, Approach, Board, Ride, Exit }
    private const float Arrival = 0.95f;
    private const float MechanicsInterval = 0.24f;
    private readonly BotBody body;
    private readonly Director director;
    private readonly NavMeshAgent agent;
    private readonly NavMeshPath path = new NavMeshPath();
    private readonly NavMeshPath candidatePath = new NavMeshPath();
    private readonly NavMeshPath spikeDetourPath = new NavMeshPath();
    private readonly Vector3[] corners = new Vector3[48];
    private readonly Vector3[] spikeCorners = new Vector3[48];
    private readonly Collider[] nearbyInteractions = new Collider[64];
    private readonly Collider shipRoom;
    private readonly float speed;
    private readonly float aimSpeed = UnityEngine.Random.Range(100f, 220f);
    private Order order;
    private float assignedAt;
    private float enteredAt;
    private float nextMechanics;
    private float nextRoute;
    private SandWormAI? threateningWorm;
    private Vector3 wormEruptionPoint;
    private float nextWormCheck;
    private bool wormEmerging;
    private bool waypointActive;
    private bool laneUsed;
    private Vector3 waypointGoal;
    private Vector3 waypointFinalGoal;
    private Vector3 previousCorner;
    private bool hasPreviousCorner;
    private float waypointDeadline;
    private float lastProgress;
    private float blockedUntil;
    private float nextKeySearch;
    private float nextExtraPickup;
    private float nextVisit;
    private float lastPortalTime = -100f;
    private float nextFacilityEntry;
    private float nextNavRecovery;
    private bool lastPortalEntered;
    private int lastExitEntranceId = -1;
    private int preferredEntranceId = -1;
    private Vector3 progressPoint;
    private Vector3 routedGoal;
    private Vector3 routeLearnStart;
    private Vector3 routeLearnGoal;
    private bool routeLearnActive;
    private DoorLock? pendingLockedDoor;
    private bool routed;
    private bool disposed;
    private float nextLook;
    private Vector3 lookPoint;
    private bool frozen;
    private bool crouching;
    private bool dogAlert;
    private MouthDogAI? pursuingDog;
    private float nextDogCheck;
    private float nextGiantDetour;
    private float thumperTurnUntil;
    private float nextThumperTurn;
    private Vector3 thumperTurnGoal;
    private bool scanning;
    private float scanAngle;
    private float scanHeight;
    private Blackboard.Contact? visibleThreat;
    private readonly float[] lookHistory = new float[8];
    private LiftPhase liftPhase;
    private bool liftGoingDown;
    private float liftDeadline;
    private float liftProgressTime;
    private Vector3 liftProgressPosition;
    private Vector3 liftOffset;
    private Vector3 liftApproach;
    private readonly System.Collections.Generic.Dictionary<(Vector3Int Cell, bool Inside), float> failedTargets = new System.Collections.Generic.Dictionary<(Vector3Int, bool), float>();
    private float pendingDoorSince;
    private Vector3 lastExtractionPosition;
    private float extractionProgressAt;
    private float nextExtractionDiagnostic;
    private float nextIndoorDiagnostic;
    private float nextLandingWarning;
    private float indoorProgressAt;
    private Vector3 lastIndoorPosition;
    private EnemyAI? grabbedBy;
    private float grabbedSince;
    private EntranceTeleport? selectedPortal;
    private EntranceTeleport? openingPortal;
    private float portalReadyAt;
    private Vector3 portalApproach;
    private int routeRecoveryAttempts;
    private GrabbableObject? lightningRetreat;
    private Shovel? swingWeapon;
    private float releaseSwingAt;
    private float nextSwing;
    private float sprintMultiplier = 1f;
    private float chargeReadyAt;
    private GrabbableObject? chargingItem;
    private float chargeStartedAt;
    private float nextChargeAttempt;
    private float recoveryUntil;
    private Vector3 recoveryGoal;
    private StunGrenadeItem? primedGrenade;
    private Vector3 grenadeTarget;
    private float nextGrenade;
    private float retreatUntil;
    private bool jesterEvacuating;
    private float turretEscapeUntil;
    private float nutcrackerEscapeUntil;
    private NutcrackerEnemyAI? coverNutcracker;
    private Vector3 coverGoal;
    private float nextCoverSearch;
    private bool coverRoute;
    private Turret? escapingTurret;
    private RadMechAI? escapingMech;
    private Vector3 retreatThreat;
    private float nextGunShot;
    private float nextEscapeDrop;
    private float nextAmmoAction;
    private float nextEquipmentSearch;
    private float nextBabyRock;
    private SpikeRoofTrap? avoidedSpike;
    private float spikeAvoidUntil;
    private float spikeAvoidLastSeen;
    private float nextSpikeDetour;
    private float spikeSprintUntil;
    private bool waitingForSpike;
    private PlayerControllerB? peeperTarget;
    private bool tulipRescue;
    private PlayerControllerB? pendingPeeperHit;
    private PlayerControllerB? pendingTulipTarget;
    private Shovel? pendingTulipShovel;
    private float peeperHitAt;
    private GunAmmo? pendingBagAmmo;
    private GrabbableObject? containerItem;
    private InteractTrigger? containerTrigger;
    private Vector3 containerPoint;
    private bool containerTried;
    private float containerAttemptUntil;
    private float containerWaitUntil;

    internal static CrewBot? Create(Director director, PlayerControllerB player, Vector3 point, float speed)
    {
        var root = new GameObject("LethalFakeClient.Navigator");
        root.transform.position = point;
        var agent = root.AddComponent<NavMeshAgent>();
        if (!agent.isOnNavMesh)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        return new CrewBot(director, player, agent, speed);
    }

    private CrewBot(Director director, PlayerControllerB player, NavMeshAgent agent, float speed)
    {
        this.director = director;
        this.agent = agent;
        this.speed = speed;
        shipRoom = player.playersManager.shipInnerRoomBounds;
        agent.speed = speed;
        agent.acceleration = 1000f;
        agent.angularSpeed = 10000f;
        agent.radius = Mathf.Max(0.4f, player.thisController.radius);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = 10 + (int)(player.playerClientId % 80);
        agent.stoppingDistance = 0.45f;
        agent.autoTraverseOffMeshLink = true;
        body = new BotBody(player);
        body.Pose(agent.transform.position, Quaternion.identity, false, false, Aboard);
        director.Add(this);
    }

    public PlayerControllerB Body => body.Player;
    public Vector3 Position => agent.transform.position;
    public bool Aboard => !Inside && shipRoom != null && shipRoom.bounds.Contains(Position + Vector3.up * 0.45f);
    public bool Inside => Body != null && Body.isInsideFactory;
    public bool Available => !disposed && Body != null && Body.IsOwnedByServer && !body.Dead && !Body.isPlayerDead && Body.isPlayerControlled;
    public bool CanGuard => Available && liftPhase == LiftPhase.None && (Aboard || agent.isOnNavMesh && Time.time >= blockedUntil);
    public bool ElevatorTransit => liftPhase != LiftPhase.None;
    private static bool IsCargo(GrabbableObject? item)
    {
        return item != null && item.IsSpawned && item.isHeld && item.itemProperties != null &&
            (item is RagdollGrabbableObject || item.itemProperties.isScrap) &&
            !(item is KeyItem) && !(item is Shovel) && !(item is CaveDwellerPhysicsProp) && !(item is BoomboxItem) && !(item is LockPicker) &&
            !(item is StunGrenadeItem) && !(item is ShotgunItem) && !(item is BeltBagItem) && !(item is GunAmmo);
    }

    public GrabbableObject? Cargo
    {
        get
        {
            foreach (var item in Body.ItemSlots)
                if (IsCargo(item))
                    return item;
            return IsCargo(Body.ItemOnlySlot) ? Body.ItemOnlySlot : null;
        }
    }
    public Order Order => order;
    public float AssignedAt => assignedAt;
    public float EnteredAt => enteredAt;
    public int PreferredEntranceId => preferredEntranceId;
    internal bool Walking => body.WalkingNow;
    internal bool TerminalUsing => body.TerminalUsing;
    internal bool LeverUsing => body.LeverUsing;
    internal void SetLeverUsing(bool value) => body.SetLeverPose(value);
    internal bool HasPeeperTarget => peeperTarget != null && !peeperTarget.isPlayerDead &&
        (tulipRescue ? Blackboard.AttachedTulip(peeperTarget) != null : PeeperCompatibility.HasAttached(peeperTarget));
    internal void AssignPeeperTarget(PlayerControllerB target, bool tulip) { peeperTarget = target; tulipRescue = tulip; }
    internal static bool SafeTulipSwing(PlayerControllerB target, Shovel shovel)
    {
        int damage = shovel.shovelHitForce <= 2 ? 20 : shovel.shovelHitForce <= 4 ? 30 : 100;
        if (target.health <= damage)
            return false;
        foreach (var player in target.playersManager.allPlayerScripts)
            if (player != null && player != target && player.isPlayerControlled && !player.isPlayerDead &&
                player.health <= damage && (player.transform.position - target.transform.position).sqrMagnitude < 9f)
                return false;
        return true;
    }
    internal void SetTerminalUsing(bool value, Vector3 terminalPoint)
    {
        body.SetTerminalPose(value);
        if (value)
        {
            scanning = false;
            lookPoint = terminalPoint + Vector3.up;
            nextLook = Time.time + 1f;
        }
    }
    internal bool Sprinting => Body.isSprinting;
    internal Vector3 LookPoint => lookPoint;
    internal bool Crouching => crouching;
    internal bool Frozen => frozen;
    internal bool Died => body.Dead || Body != null && Body.isPlayerDead;
    internal BotBody PhysicalBody => body;

    public bool CanAttempt(Vector3 point, bool inside, float now)
    {
        var key = (Vector3Int.FloorToInt(point / 3f), inside);
        if (!failedTargets.TryGetValue(key, out float retryAt))
            return true;
        if (now < retryAt)
            return false;
        failedTargets.Remove(key);
        return true;
    }

    public bool CanEnterFacility(float now) => now >= nextFacilityEntry && director.Board.ActiveJester == null;

    public bool CanReach(Vector3 point, bool inside)
    {
        if (!Available || !agent.enabled || !agent.isOnNavMesh || inside != Inside)
            return false;
        if (inside && director.DifferentElevatorFloors(Position, point))
            return true;
        if (!NavMesh.SamplePosition(point, out var hit, 2f, agent.areaMask) ||
            Mathf.Abs(hit.position.y - point.y) >= 2f || !agent.CalculatePath(hit.position, candidatePath))
            return false;
        if (candidatePath.status == NavMeshPathStatus.PathComplete)
            return true;
        if (!(Body.currentlyHeldObjectServer is KeyItem) && body.FindItem<LockPicker>() == null)
            return false;
        int count = candidatePath.GetCornersNonAlloc(corners);
        return count > 0 && count < corners.Length && director.LockedDoorNear(corners[count - 1]) != null;
    }

    public bool TryPortalApproach(Vector3 point, out Vector3 approach)
    {
        approach = default;
        if (!Available || !agent.enabled || !agent.isOnNavMesh)
            return false;
        if (Inside && director.DifferentElevatorFloors(Position, point) &&
            NavMesh.SamplePosition(point, out var elevatorHit, 3f, agent.areaMask) &&
            Mathf.Abs(elevatorHit.position.y - point.y) < 3f)
        {
            approach = elevatorHit.position;
            return true;
        }

        // Entrance transforms can be inside the door or above a stair landing.
        // Search the walkable area around the trigger, not just its origin.
        Vector3 partialApproach = default;
        float partialDistance = float.PositiveInfinity;
        for (int i = 0; i < 17; i++)
        {
            float radius = i == 0 ? 0f : i <= 8 ? 1.5f : 2.8f;
            float angle = (i - 1) * (Mathf.PI / 4f);
            Vector3 candidate = point + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (!NavMesh.SamplePosition(candidate, out var hit, 1.4f, agent.areaMask) ||
                Mathf.Abs(hit.position.y - point.y) > 3f)
                continue;
            Vector3 delta = hit.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude > 12.25f || !agent.CalculatePath(hit.position, candidatePath))
                continue;
            if (candidatePath.status == NavMeshPathStatus.PathComplete)
            {
                approach = hit.position;
                return true;
            }
            int count = candidatePath.GetCornersNonAlloc(corners);
            if (count == 0 || count == corners.Length)
                continue;
            Vector3 end = corners[count - 1];
            if ((Body.currentlyHeldObjectServer is KeyItem || body.FindItem<LockPicker>() != null) && director.LockedDoorNear(end) != null)
            {
                approach = hit.position;
                return true;
            }
            float remaining = (end - point).sqrMagnitude;
            if (remaining < partialDistance && (end - Position).sqrMagnitude > 4f &&
                remaining + 4f < (Position - point).sqrMagnitude)
            {
                partialDistance = remaining;
                partialApproach = end;
            }
        }
        if (float.IsPositiveInfinity(partialDistance))
            return false;
        approach = partialApproach;
        return true;
    }

    public void Assign(Order next)
    {
        if (Aboard && (next.Kind == TaskKind.Hold || next.Kind == TaskKind.Guard || next.Kind == TaskKind.Return))
            next = new Order(next.Kind, director.Ship);

        if (order.Kind == next.Kind && order.Partner == next.Partner && order.Item == next.Item &&
            order.Inside == next.Inside && (order.Point - next.Point).sqrMagnitude < 1f)
            return;

        CloseOpeningPortal();

        if (order.Kind != next.Kind || order.Item != next.Item || order.Inside != next.Inside ||
            (order.Point - next.Point).sqrMagnitude >= 4f)
            routeLearnActive = false;

        order = next;
        waypointActive = false;
        laneUsed = false;
        hasPreviousCorner = false;
        selectedPortal = null;
        routeRecoveryAttempts = 0;
        recoveryUntil = 0f;
        assignedAt = Time.time;
        if (next.Kind == TaskKind.Explore && next.Inside && !Inside)
            Plugin.Log.LogInfo($"Bot {Body.playerClientId} is heading to a facility entrance, destination {next.Point}.");
        routed = false;
        nextRoute = 0f;
        ResetPath();
    }

    internal void Tick(float now)
    {
        if (!Available)
            return;

        if (openingPortal != null && now > portalReadyAt + 3f)
            CloseOpeningPortal();

        if (now >= nextWormCheck)
        {
            nextWormCheck = now + 0.15f;
            var worm = !Inside && !Aboard ? Blackboard.ThreateningWorm(Body, Position) : null;
            if (worm == null && !Inside && !Aboard && wormEmerging && threateningWorm != null &&
                !threateningWorm.isEnemyDead && threateningWorm.isActiveAndEnabled &&
                (threateningWorm.inEmergingState || threateningWorm.emerged))
            {
                Vector3 offset = Position - wormEruptionPoint;
                float height = Mathf.Abs(offset.y);
                offset.y = 0f;
                if (MovementPolicy.WormThreat(false, true, offset.sqrMagnitude, height))
                    worm = threateningWorm;
            }
            if (worm != threateningWorm)
            {
                threateningWorm = worm;
                wormEmerging = false;
                if (worm != null && agent.enabled && agent.isOnNavMesh)
                {
                    ResetPath();
                    nextRoute = 0f;
                    waypointActive = false;
                    recoveryUntil = 0f;
                }
            }
            if (worm != null && (worm.inEmergingState || worm.emerged) && !wormEmerging)
            {
                wormEruptionPoint = worm.transform.position;
                wormEmerging = true;
            }
            else if (worm != null && !worm.inEmergingState && !worm.emerged)
                wormEmerging = false;
        }

        if (now >= nextDogCheck)
        {
            nextDogCheck = now + 0.2f;
            bool previouslyAlert = dogAlert;
            dogAlert = !Aboard && director.Board.DogNearby(Position, Inside, Body, dogAlert, out pursuingDog);
            if (!dogAlert)
                pursuingDog = null;
            var giant = !Aboard ? director.Board.NearbyForestGiant(Position, Inside) : null;
            bool giantChasing = giant != null && giant.currentBehaviourStateIndex == 1 && giant.chasingPlayer == Body;
            bool giantFacing = giant != null && GiantFacing(giant) && !Physics.Linecast(
                giant.eye != null ? giant.eye.position : giant.transform.position + Vector3.up * 4f, Body.gameplayCamera.transform.position, director.Board.SightMask, QueryTriggerInteraction.Ignore);
            var immediate = dogAlert || giantFacing ? director.Board.NearbyFleeing(Position, Inside, Body, now) : null;
            bool otherDanger = immediate != null && !(immediate.Source is MouthDogAI) &&
                ((Position - immediate.Sample.Position).sqrMagnitude < 81f || immediate.Sample.Pursuing == Body);
            bool lightningDanger = !Inside && !Aboard && LightningHazard.IsTarget(LightningHazard.Target) &&
                (Position - LightningHazard.Target!.transform.position).sqrMagnitude < 64f;
            crouching = threateningWorm == null && !giantChasing && (dogAlert && pursuingDog == null || giantFacing) && !otherDanger && !lightningDanger;
            if (crouching)
            {
                Body.isSprinting = false;
                sprintMultiplier = 1f;
                if (agent.enabled && agent.isOnNavMesh)
                    agent.speed = Mathf.Min(agent.speed, speed * 0.67f / Mathf.Max(1f, Body.carryWeight));
                if (dogAlert)
                    body.PocketNoisyItem();
                var noisy = body.DangerousNoisyCargo();
                if (dogAlert && noisy != null && Blackboard.DogProximityRisk(Position, Inside) < 0.2f &&
                    director.Board.Risk(Position, Inside, now, false, false) < 0.35f && body.DropItem(noisy))
                    director.StashNoisyLoot(this, noisy, now);
            }
            body.SetCrouching(crouching);
            if (dogAlert != previouslyAlert)
                director.Record(this, "dog_alert", $"active={dogAlert} crouching={crouching}");
        }

        bool staminaWalking = agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f &&
            Body.inAnimationWithEnemy == null && liftPhase != LiftPhase.Ride;
        Body.sprintMeter = StaminaPolicy.Update(Body.sprintMeter, Time.deltaTime, Body.sprintTime,
            Body.carryWeight, Body.isSprinting && staminaWalking, staminaWalking, Body.isMovementHindered > 0);
        if (Body.sprintMeter <= 0.1f)
            Body.isExhausted = true;
        else if (Body.sprintMeter > 0.2f)
            Body.isExhausted = false;

        if (!Inside && !Aboard && body.DropLightningTarget())
        {
            lightningRetreat = LightningHazard.Target;
            ResetPath();
            director.RequestReplan();
        }
        body.EquipStoredWeapon();
        if (pendingPeeperHit != null && now >= peeperHitAt)
        {
            var target = pendingPeeperHit;
            pendingPeeperHit = null;
            if (!target.isPlayerDead && target.health > 1 && PeeperCompatibility.HasAttached(target) &&
                (target.transform.position - Position).sqrMagnitude < 9f)
            {
                int damage = Mathf.Min(10, target.health - 1);
                if (BotBody.TryGet(target, out var targetBody))
                {
                    targetBody.ApplyDamage(damage, CauseOfDeath.Bludgeoning, 0, Vector3.zero, true);
                    PeeperCompatibility.EjectFromBot(target);
                }
                else
                    target.DamagePlayerFromOtherClientServerRpc(damage, (target.transform.position - Position).normalized, (int)Body.playerClientId);
            }
        }
        if (pendingTulipTarget != null && now >= peeperHitAt)
        {
            var target = pendingTulipTarget;
            var shovel = pendingTulipShovel;
            pendingTulipTarget = null;
            pendingTulipShovel = null;
            var snake = Blackboard.AttachedTulip(target);
            if (snake != null && shovel != null && !target.isPlayerDead && SafeTulipSwing(target, shovel) &&
                (target.transform.position - Position).sqrMagnitude < 9f)
                snake.HitEnemyOnLocalClient(shovel.shovelHitForce, (snake.transform.position - Position).normalized, Body, true);
        }
        if (swingWeapon != null && now >= releaseSwingAt)
        {
            swingWeapon.ItemActivate(false, false);
            swingWeapon = null;
        }

        var enemy = Body.inAnimationWithEnemy;
        if (enemy != null)
        {
            if (grabbedBy != enemy)
            {
                grabbedBy = enemy;
                grabbedSince = now;
            }
            bool staleGrab = enemy.isEnemyDead || !enemy.gameObject.activeInHierarchy ||
                now - grabbedSince > 12f && !enemy.inSpecialAnimation && !Body.inSpecialInteractAnimation;
            if (!staleGrab)
            {
                ResetPath();
                if (agent.enabled && NavMesh.SamplePosition(Body.transform.position, out var grabbedPosition, 2f, agent.areaMask))
                    agent.Warp(grabbedPosition.position);
                return;
            }
            Body.inAnimationWithEnemy = null;
            grabbedBy = null;
            ResetPath();
            director.RequestReplan();
        }
        else
            grabbedBy = null;
        if (liftPhase == LiftPhase.Board || liftPhase == LiftPhase.Ride || liftPhase == LiftPhase.Exit)
        {
            TickElevator(now);
            return;
        }
        if (!agent.enabled)
        {
            if (now < blockedUntil || !NavMesh.SamplePosition(Body.transform.position, out var recovery, 2f, agent.areaMask) ||
                Mathf.Abs(recovery.position.y - Body.transform.position.y) > 1.5f)
                return;
            agent.transform.position = recovery.position;
            agent.enabled = true;
        }
        if (!agent.isOnNavMesh)
        {
            if (now >= nextNavRecovery)
            {
                nextNavRecovery = now + 2f;
                if (NavMesh.SamplePosition(Body.transform.position, out var recovery, 1.5f, agent.areaMask) &&
                    Mathf.Abs(recovery.position.y - Body.transform.position.y) < 1.5f && agent.Warp(recovery.position))
                {
                    ResetPath();
                    director.RequestReplan();
                }
            }
            return;
        }

        bool waitingAtSpike = UpdateSpikeMovement(now);
        CheckExtractionProgress(now);
        CheckIndoorProgress(now);
        if (!waitingAtSpike && now >= nextMechanics)
        {
            nextMechanics = now + MechanicsInterval;
            if (!agent.isOnOffMeshLink)
            {
                if (threateningWorm != null && !Inside && !Aboard)
                {
                    Vector3 danger = wormEmerging ? wormEruptionPoint : threateningWorm.transform.position;
                    DropHeavyForEscape(Vector3.Distance(Position, danger), true, now);
                    Escape(danger, now);
                }
                else
                    Act(now);
            }
        }

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;
        bool walking = velocity.sqrMagnitude > 0.04f;
        if (walking && scanning)
        {
            Vector3 forward = agent.desiredVelocity.sqrMagnitude > 0.1f ? agent.desiredVelocity.normalized : velocity.normalized;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f)
                lookPoint = Position + Quaternion.Euler(0f, Mathf.Clamp(scanAngle, -45f, 45f), 0f) * forward.normalized * 12f + Vector3.up * scanHeight;
        }
        bool sprinting = walking && !crouching && (threateningWorm != null || now < spikeSprintUntil || order.Kind == TaskKind.Evade || Inside && now < retreatUntil || now < turretEscapeUntil || now < nutcrackerEscapeUntil || !Inside && !Aboard) &&
            StaminaPolicy.CanSprint(Body.sprintMeter, Body.isSprinting && staminaWalking, Body.criticallyInjured,
                Body.isMovementHindered > 0, !Inside && !Aboard && threateningWorm == null);
        sprintMultiplier = Mathf.Lerp(sprintMultiplier, sprinting ? 2.25f : 1f, Time.deltaTime * (sprinting ? 1f : 10f));
        if (sprinting && (now < spikeSprintUntil || threateningWorm != null))
            sprintMultiplier = 2.25f;
        agent.speed = speed * (crouching ? 0.67f : sprintMultiplier) / Mathf.Max(1f, Body.carryWeight);
        Quaternion rotation = walking && !frozen ? Quaternion.RotateTowards(Body.transform.rotation, Quaternion.LookRotation(velocity), 360f * Time.deltaTime) : Body.transform.rotation;
        if (!frozen && (lookPoint - Position).sqrMagnitude > 0.01f)
        {
            Vector3 lookDirection = lookPoint - Position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.01f)
                rotation = Quaternion.RotateTowards(Body.transform.rotation, Quaternion.LookRotation(lookDirection), aimSpeed * Time.deltaTime);
        }
        var lookingThreat = visibleThreat;
        if (!frozen && lookingThreat != null && lookingThreat.Source is SpringManAI && Blackboard.Watch(lookingThreat))
        {
            Vector3 towards = lookingThreat.Sample.Position - Position;
            towards.y = 0f;
            if (towards.sqrMagnitude > 0.01f)
                rotation = Quaternion.RotateTowards(Body.transform.rotation, Quaternion.LookRotation(towards), 360f * Time.deltaTime);
        }
        body.Pose(Position, rotation, walking, Inside, Aboard, sprinting, velocity);
        body.SetCrouching(crouching);
        if (!frozen)
            body.SetLook(lookPoint, aimSpeed);
    }

    private bool UpdateSpikeMovement(float now)
    {
        if (!Inside || !agent.hasPath || agent.isOnOffMeshLink)
        {
            if (waitingForSpike && agent.enabled && agent.isOnNavMesh)
                agent.isStopped = false;
            waitingForSpike = false;
            return false;
        }
        var trap = director.Board.Spikes.Crossing(Position, agent.steeringTarget, true);
        Vector3 center = trap != null && trap.laserEye != null ? trap.laserEye.position : trap != null ? trap.transform.position : Position;
        Vector3 offset = center - Position;
        offset.y = 0f;
        if (trap == null || offset.sqrMagnitude > 36f)
        {
            if (waitingForSpike)
                agent.isStopped = false;
            waitingForSpike = false;
            return false;
        }
        bool alreadyInside = director.Board.Spikes.Contains(trap, Position);
        float crossingSpeed = Mathf.Max(1f, speed * 2.25f / Mathf.Max(1f, Body.carryWeight));
        float crossingSeconds = Mathf.Clamp((Mathf.Sqrt(offset.sqrMagnitude) + 2f) / crossingSpeed, 0.5f, 2.5f);
        bool staminaReady = Body.sprintMeter > 0.3f || Body.criticallyInjured || Body.isMovementHindered > 0;
        bool wait = !alreadyInside && (!SpikeTrapSafety.SafeToEnter(trap, crossingSeconds) || !staminaReady);
        if (wait)
        {
            var threat = director.Board.NearbyFleeing(Position, Inside, Body, now);
            if (director.Board.DangerousTurret(Position, Inside, Body) != null || threat != null &&
                (threat.Sample.Pursuing == Body || (threat.Sample.Position - Position).sqrMagnitude < 36f))
                wait = false;
        }
        if (wait)
        {
            agent.isStopped = true;
            waitingForSpike = true;
            lastProgress = now;
            return true;
        }
        if (waitingForSpike)
            agent.isStopped = false;
        waitingForSpike = false;
        spikeSprintUntil = now + 1.2f;
        return false;
    }

    private void CheckExtractionProgress(float now)
    {
        if (!director.IsLate() || Aboard || waitingForSpike || liftPhase != LiftPhase.None ||
            order.Kind != TaskKind.Return && order.Kind != TaskKind.Transport && order.Kind != TaskKind.Guard)
        {
            lastExtractionPosition = Position;
            extractionProgressAt = now;
            return;
        }
        if ((Position - lastExtractionPosition).sqrMagnitude >= 1f)
        {
            lastExtractionPosition = Position;
            extractionProgressAt = now;
            return;
        }
        if (now - extractionProgressAt < 10f || frozen || now < nextExtractionDiagnostic)
            return;
        nextExtractionDiagnostic = now + 15f;
        extractionProgressAt = now;
        Plugin.Log.LogWarning($"Bot {Body.playerClientId} has not progressed during extraction; replanning (task={order.Kind}, inside={Inside}, cargo={Cargo != null}, path={agent.pathStatus}).");
        selectedPortal = null;
        routeRecoveryAttempts = 0;
        ResetPath();
        director.RequestReplan();
    }

    private void CheckIndoorProgress(float now)
    {
        if (!Inside || waitingForSpike || liftPhase != LiftPhase.None || frozen || Body.inSpecialInteractAnimation || now < containerWaitUntil ||
            agent.isOnOffMeshLink || pendingLockedDoor != null && pendingLockedDoor.isPickingLock ||
            order.Kind == TaskKind.Escort && order.Partner != null && order.Partner.Available && order.Partner.Inside)
        {
            lastIndoorPosition = Position;
            indoorProgressAt = now;
            return;
        }
        if ((Position - lastIndoorPosition).sqrMagnitude >= 0.04f)
        {
            lastIndoorPosition = Position;
            indoorProgressAt = now;
            return;
        }
        if (now - indoorProgressAt < 3f)
            return;
        indoorProgressAt = now;
        if (now >= nextIndoorDiagnostic)
        {
            nextIndoorDiagnostic = now + 15f;
            Plugin.Log.LogWarning($"Bot {Body.playerClientId} is idle inside; seeking a nearby route (task={order.Kind}, cargo={Cargo != null}, path={agent.pathStatus}).");
        }
        if (!TryIndoorRecovery(now))
        {
            selectedPortal = null;
            ResetPath();
            nextRoute = 0f;
            director.RequestReplan();
        }
    }

    private bool TryIndoorRecovery(float now)
    {
        var nodes = director.IndoorNodes;
        int start = nodes.Count > 0 ? UnityEngine.Random.Range(0, nodes.Count) : 0;
        int attempted = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int offset = 0; offset < nodes.Count; offset++)
            {
                Vector3 point = nodes[(start + offset) % nodes.Count];
                Vector3 delta = point - Position;
                if (Mathf.Abs(delta.y) > 2.5f || delta.sqrMagnitude < 2.25f ||
                    delta.sqrMagnitude > (pass == 0 ? 64f : 400f) ||
                    !director.RecoveryDestinationAllowed(this, point, now) || !CanAttempt(point, true, now))
                    continue;
                if (++attempted > 24)
                    break;
                if (StartIndoorRecovery(point, now))
                    return true;
            }
        }
        float directionOffset = UnityEngine.Random.Range(0f, 360f);
        for (int index = 0; index < 8; index++)
        {
            Vector3 point = Position + Quaternion.Euler(0f, directionOffset + index * 45f, 0f) * Vector3.forward * 3f;
            if (!NavMesh.SamplePosition(point, out var hit, 1.5f, agent.areaMask) ||
                Mathf.Abs(hit.position.y - Position.y) > 1.5f || (hit.position - Position).sqrMagnitude < 1f ||
                !director.RecoveryDestinationAllowed(this, hit.position, now) || !CanAttempt(hit.position, true, now))
                continue;
            if (StartIndoorRecovery(hit.position, now))
                return true;
        }
        return false;
    }

    private bool StartIndoorRecovery(Vector3 point, float now)
    {
        if (!TryRoute(point, now, false))
            return false;
        recoveryGoal = point;
        recoveryUntil = now + 6f;
        director.Record(this, "idle_recovery", $"target={point} task={order.Kind}");
        return true;
    }

    private void Act(float now)
    {
        if (!Inside && !Aboard && lightningRetreat == null && LightningHazard.IsTarget(LightningHazard.Target) &&
            (Position - LightningHazard.Target!.transform.position).sqrMagnitude < 64f)
        {
            lightningRetreat = LightningHazard.Target;
            ResetPath();
        }
        if (!Inside && !Aboard && LightningHazard.IsTarget(lightningRetreat) &&
            (Position - lightningRetreat!.transform.position).sqrMagnitude < 64f)
        {
            Escape(lightningRetreat.transform.position, now);
            return;
        }
        lightningRetreat = null;
        bool retreatingIntent = Inside && (now < retreatUntil || order.Kind == TaskKind.Evade);
        visibleThreat = Aboard ? null : director.Board.Tactical(Position, Inside, Body, now);
        if (Inside && now >= nextVisit)
        {
            director.RememberVisit(Body, Position, now);
            nextVisit = now + 0.8f;
        }
        var turretDanger = director.Board.DangerousTurret(Position, Inside, Body);
        var nutcrackerDanger = !Aboard ? Blackboard.TargetingNutcracker(Body, Inside) : null;
        frozen = turretDanger == null && nutcrackerDanger == null && visibleThreat != null && Blackboard.Freeze(visibleThreat) &&
            !Physics.Linecast(Body.gameplayCamera.transform.position, visibleThreat.Sample.Position + Vector3.up,
                director.Board.SightMask, QueryTriggerInteraction.Ignore);
        if (turretDanger != null)
        {
            turretEscapeUntil = now + 1f;
            DropHeavyForEscape(0f, true, now);
            if (primedGrenade != null)
                UpdateGrenade(null, now);
            EscapeTurret(turretDanger, now);
            return;
        }
        var mechDanger = director.Board.MechSeeing(Position, Inside);
        if (mechDanger != null && !Aboard)
        {
            DropHeavyForEscape(Vector3.Distance(Position, mechDanger.transform.position), true, now);
            EscapeMech(mechDanger, now);
            return;
        }
        if (nutcrackerDanger != null)
        {
            crouching = false;
            body.SetCrouching(false);
            nutcrackerEscapeUntil = now + 0.5f;
            if (primedGrenade != null)
                UpdateGrenade(null, now);
            EscapeNutcracker(nutcrackerDanger, now);
            return;
        }
        coverNutcracker = null;
        if (frozen)
        {
            if (primedGrenade != null)
                UpdateGrenade(null, now);
            ResetPath();
            return;
        }

        if (!Aboard && director.Board.AreaRisk(Position, Inside) >= 0.5f && TryLeaveHazardArea(now))
            return;

        var jester = director.Board.ActiveJester;
        if (Inside && jester != null)
        {
            if (!jesterEvacuating)
            {
                jesterEvacuating = true;
                selectedPortal = null;
                ResetPath();
                director.PlanEscape(this, now, true);
            }
            retreatUntil = Mathf.Max(retreatUntil, now + 1f);
            retreatThreat = jester.transform.position;
            if (!TryEscapeExit(now, retreatThreat, false))
                Escape(retreatThreat, now);
            return;
        }
        jesterEvacuating = false;

        var targetingPuma = Aboard ? null : director.Board.TargetingPuma(Body, Position, Inside);

        if (visibleThreat != null && Blackboard.Watch(visibleThreat))
        {
            scanning = false;
            lookPoint = visibleThreat.Sample.Position + Vector3.up * 1.2f;
            nextLook = now + 1f;
            if (visibleThreat.Source is FlowermanAI bracken && bracken.currentBehaviourStateIndex == 0 &&
                Body.HasLineOfSightToPosition(bracken.transform.position + Vector3.up * 0.5f, 30f, 30))
            {
                bracken.SwitchToBehaviourState(1);
                bracken.evadeStealthTimer = 0f;
            }
        }
        else if (targetingPuma != null)
        {
            scanning = false;
            lookPoint = targetingPuma.eye != null ? targetingPuma.eye.position : targetingPuma.transform.position + Vector3.up;
            nextLook = now + 1f;
        }
        else if (visibleThreat != null && Blackboard.LookAway(visibleThreat))
        {
            scanning = false;
            lookPoint = Position + (Position - visibleThreat.Sample.Position).normalized * 10f + Vector3.up;
            nextLook = now + 1f;
        }
        else if (Inside && order.Kind == TaskKind.Escort && order.Partner != null && order.Partner.Available &&
            order.Partner.Inside && agent.velocity.sqrMagnitude <= 0.04f)
        {
            Vector3 direction = -order.Partner.Body.gameplayCamera.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
            {
                scanning = false;
                lookPoint = Position + direction.normalized * 12f + Vector3.up * 1.4f;
            }
        }
        else if (now >= nextLook)
        {
            Vector3 forward = agent.desiredVelocity.sqrMagnitude > 0.1f ? agent.desiredVelocity.normalized : Body.transform.forward;
            int selected = 0;
            float best = float.NegativeInfinity;
            int sector = 0;
            bool moving = agent.velocity.sqrMagnitude > 0.04f;
            int sectorCount = moving ? 3 : lookHistory.Length;
            for (; sector < sectorCount; ++sector)
            {
                float angle = moving ? (sector - 1) * 45f : sector * 45f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
                float score = now - lookHistory[sector] + director.Board.Risk(Position + direction * 10f, Inside, now) * 12f;
                if (order.Partner != null && order.Partner.Available)
                {
                    Vector3 partnerForward = order.Partner.Body.gameplayCamera.transform.forward;
                    score -= Vector3.Dot(direction, partnerForward) * 8f;
                }
                if (score > best)
                {
                    best = score;
                    selected = sector;
                }
            }
            lookHistory[selected] = now;
            scanAngle = moving ? (selected - 1) * 45f : Mathf.DeltaAngle(0f, selected * 45f);
            scanHeight = UnityEngine.Random.Range(0.7f, 2.5f);
            scanning = true;
            lookPoint = Position + Quaternion.Euler(0f, scanAngle, 0f) * forward * 12f + Vector3.up * scanHeight;
            nextLook = now + UnityEngine.Random.Range(1.5f, 3.5f);
        }
        if (!retreatingIntent && !Aboard && pendingBagAmmo == null && now >= nextAmmoAction && body.FindItem<ShotgunItem>() is ShotgunItem carriedGun)
        {
            foreach (var candidate in Blackboard.Items)
            {
                if (candidate is GunAmmo ammo && ammo.ammoType == carriedGun.gunCompatibleAmmoID &&
                    candidate.isActiveAndEnabled && candidate.IsSpawned && candidate.grabbable && !candidate.isHeld &&
                    !candidate.heldByPlayerOnServer && !candidate.isHeldByEnemy && candidate.isInFactory == Inside &&
                    (Position - candidate.transform.position).sqrMagnitude < 2.25f &&
                    !Physics.Linecast(Position + Vector3.up, candidate.transform.position, director.Board.SightMask, QueryTriggerInteraction.Ignore))
                {
                    nextAmmoAction = now + 2f;
                    CollectAmmo(ammo);
                    break;
                }
            }
        }
        if (!retreatingIntent && Body.currentlyHeldObjectServer == null && now >= nextKeySearch)
        {
            nextKeySearch = now + 2f;
            foreach (var candidate in Blackboard.Items)
            {
                if ((Inside && candidate is KeyItem || !Aboard && candidate is Shovel && !candidate.isInShipRoom) && candidate.isActiveAndEnabled && candidate.IsSpawned && candidate.grabbable && !candidate.isHeld && !candidate.heldByPlayerOnServer && !candidate.isHeldByEnemy &&
                    (Position - candidate.transform.position).sqrMagnitude < 2.25f &&
                    !Physics.Linecast(Position + Vector3.up, candidate.transform.position, director.Board.SightMask, QueryTriggerInteraction.Ignore))
                {
                    body.Grab(candidate);
                    break;
                }
            }
        }
        var contact = director.Board.Nearby(Position, Inside, now);
        var danger = director.Board.NearbyFleeing(Position, Inside, Body, now);
        if (danger != null && ((Position - danger.Sample.Position).sqrMagnitude < 81f || danger.Sample.Pursuing == Body))
            contact = danger;
        if (!retreatingIntent && !Aboard && now >= nextEquipmentSearch && (contact == null ||
            !Blackboard.MustFlee(contact, Body) || (Position - contact.Sample.Position).sqrMagnitude > 144f))
        {
            nextEquipmentSearch = now + 2f;
            foreach (var candidate in Blackboard.Items)
            {
                bool useful = candidate is ShotgunItem && body.FindItem<ShotgunItem>() == null ||
                    candidate is BeltBagItem && body.FindItem<BeltBagItem>() == null;
                if (!useful || !candidate.isActiveAndEnabled || !candidate.IsSpawned || !candidate.grabbable ||
                    candidate.isHeld || candidate.heldByPlayerOnServer || candidate.isHeldByEnemy || candidate.isInFactory != Inside ||
                    LightningHazard.IsTarget(candidate) || (Position - candidate.transform.position).sqrMagnitude > 2.25f ||
                    !body.CanCarryAfterPocket(candidate) || Physics.Linecast(Position + Vector3.up, candidate.transform.position,
                        director.Board.SightMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (body.PreparePickup(candidate) && body.Grab(candidate))
                    director.RequestReplan();
                break;
            }
        }
        if (!retreatingIntent)
            MaintainShotgun(contact, now);
        bool grenadeInUse = UpdateGrenade(contact, now);
        bool slimeMusic = !grenadeInUse && UpdateBoombox(contact, now);
        if (!retreatingIntent && Cargo != null && !(Cargo is RagdollGrabbableObject) && !Aboard &&
            (contact == null || !Blackboard.MustFlee(contact, Body) || (Position - contact.Sample.Position).sqrMagnitude >= 144f) &&
            now >= nextExtraPickup)
        {
            nextExtraPickup = now + 1f;
            foreach (var candidate in Blackboard.Items)
            {
                if (candidate == null || candidate is RagdollGrabbableObject || candidate.itemProperties == null ||
                    candidate.itemProperties.twoHanded || (Position - candidate.transform.position).sqrMagnitude > 2.25f ||
                    crouching && NoisyWhenHeld(candidate) ||
                    !body.CanCarryAfterPocket(candidate) ||
                    !PickupVisible(Position + Vector3.up * 1.25f, candidate, PickupPoint(candidate)) ||
                    !director.CanCollectNearby(this, candidate, now))
                    continue;
                if (FocusPickup(candidate, now) && body.PreparePickup(candidate) && body.Grab(candidate))
                    director.RequestReplan();
                break;
            }
        }
        if (!grenadeInUse && !slimeMusic && BotSettings.Defend && contact != null && Blackboard.MustFlee(contact, Body) &&
            (Position - contact.Sample.Position).sqrMagnitude < 16f && !(Body.currentlyHeldObjectServer is Shovel) &&
            (body.FindItem<ShotgunItem>() == null || body.FindItem<ShotgunItem>()!.shellsLoaded <= 0))
            body.EquipWeapon();
        bool attacked = !grenadeInUse && !slimeMusic && TryAttack(contact, now);
        bool immediateDanger = !Aboard && contact != null && Blackboard.MustFlee(contact, Body) &&
            ((Position - contact.Sample.Position).sqrMagnitude < 64f || contact.Sample.Pursuing == Body);
        bool maintainingGaze = targetingPuma != null || visibleThreat != null && (Blackboard.Watch(visibleThreat) || Blackboard.Freeze(visibleThreat));
        if (BotSettings.Defend && !attacked && !immediateDanger && !maintainingGaze &&
            director.Board.NearbyTulipSnake(Position, Inside) is FlowerSnakeEnemy tulip && tulip.clingingToPlayer == null)
        {
            if (!TryShoot(tulip, tulip.transform.position, now))
            {
                if (!(Body.currentlyHeldObjectServer is Shovel))
                    body.EquipWeapon();
                TryAttackEnemy(tulip, tulip.transform.position, now);
            }
        }
        if (Aboard)
            contact = null;
        if (Aboard && ChargeEquipment(now))
            return;
        if (Aboard && Cargo == null && StoreUnusedEquipment(now))
            return;
        bool fleeing = !slimeMusic && contact != null && Blackboard.MustFlee(contact, Body) &&
            ((Position - contact.Sample.Position).sqrMagnitude < 81f || contact.Sample.Pursuing == Body);
        if (pursuingDog != null && !Inside && !Aboard && !pursuingDog.isEnemyDead)
        {
            DropHeavyForEscape(Vector3.Distance(Position, pursuingDog.transform.position), true, now);
            if (BotSettings.Defend && !attacked && !grenadeInUse)
            {
                if (!TryShoot(pursuingDog, pursuingDog.transform.position, now))
                {
                    if (!(Body.currentlyHeldObjectServer is Shovel))
                        body.EquipWeapon();
                    TryAttackEnemy(pursuingDog, pursuingDog.transform.position, now);
                }
            }
            Escape(pursuingDog.transform.position, now);
            return;
        }
        if (fleeing && Inside)
        {
            DropHeavyForEscape(Vector3.Distance(Position, contact!.Sample.Position), contact.Sample.Pursuing == Body, now);
            if (now >= retreatUntil)
                director.PlanEscape(this, now, true);
            retreatUntil = now + 60f;
            retreatThreat = contact!.Sample.Position;
        }
        bool retreating = Inside && now < retreatUntil;
        if ((fleeing || retreating) && Inside && agent.hasPath)
            director.OpenNearbyDoor(this, agent.steeringTarget, now, true);
        if (fleeing && Inside)
            director.CloseDoorBehind(this, contact!.Sample.Position, now);
        if (fleeing && Inside && contact!.Source is CrawlerAI thumper && TryThumperTurn(thumper, now))
            return;
        if (retreating)
        {
            if (!TryEscapeExit(now, fleeing ? contact!.Sample.Position : retreatThreat, fleeing))
                Escape(fleeing ? contact!.Sample.Position : retreatThreat, now);
            return;
        }
        if (fleeing)
        {
            DropHeavyForEscape(Vector3.Distance(Position, contact!.Sample.Position), contact.Sample.Pursuing == Body, now);
            if (contact!.Source is ForestGiantAI giant && TryForestEscape(giant, now))
                return;
            Escape(contact!.Sample.Position, now);
            return;
        }
        if (!Inside && !Aboard && director.Board.NearbyForestGiant(Position, false) is ForestGiantAI nearbyGiant &&
            TryForestDetour(nearbyGiant, now))
            return;
        if (recoveryUntil > 0f)
        {
            if (now < recoveryUntil && (Position - recoveryGoal).sqrMagnitude > 0.5f && agent.hasPath)
                return;
            recoveryUntil = 0f;
            ResetPath();
            nextRoute = 0f;
            if (order.Kind == TaskKind.Hold)
                director.RequestReplan();
        }
        if (order.Kind == TaskKind.ShipControl && Aboard)
        {
            if ((Position - order.Point).sqrMagnitude > 1f)
                MoveTo(order.Point, now);
            else
                ResetPath();
            return;
        }
        if (order.Kind == TaskKind.AssistPeeper)
        {
            var target = peeperTarget;
            if (target == null || target.isPlayerDead || !target.isInHangarShipRoom || !(tulipRescue ? Blackboard.AttachedTulip(target) != null : PeeperCompatibility.HasAttached(target)))
            {
                peeperTarget = null;
                Finish(0f);
                return;
            }
            if (!body.EquipWeapon() || !(Body.currentlyHeldObjectServer is Shovel shovel))
            {
                Finish(0f);
                return;
            }
            if (Blackboard.AttachedTulip(target) != null && !SafeTulipSwing(target, shovel))
            {
                peeperTarget = null;
                Finish(0f);
                return;
            }
            Vector3 aim = target.transform.position + Vector3.up;
            if ((Position - target.transform.position).sqrMagnitude > 4f)
            {
                MoveTo(target.transform.position, now);
                return;
            }
            ResetPath();
            scanning = false;
            lookPoint = aim;
            nextLook = now + 1f;
            if (now >= nextSwing && !shovel.reelingUp && Vector3.Angle(Body.gameplayCamera.transform.forward, aim - Body.gameplayCamera.transform.position) < 25f)
            {
                shovel.ItemActivate(true, true);
                swingWeapon = shovel;
                releaseSwingAt = now + 0.4f;
                nextSwing = now + 1.5f;
                if (tulipRescue)
                {
                    pendingTulipTarget = target;
                    pendingTulipShovel = shovel;
                    peeperHitAt = now + 0.85f;
                }
                else
                {
                    pendingPeeperHit = target;
                    peeperHitAt = now + 0.85f;
                }
            }
            return;
        }
        if (order.Kind == TaskKind.CareBaby && order.Item is CaveDwellerPhysicsProp babyProp)
        {
            var baby = babyProp.caveDwellerScript;
            if (baby == null || baby.isEnemyDead || baby.currentBehaviourStateIndex != 0)
            {
                Finish(0f);
                return;
            }
            if (babyProp.playerHeldBy == Body && babyProp.isHeld)
            {
                ResetPath();
                if (!baby.babyCrying)
                {
                    babyProp.StopRockingBabyServerRpc();
                    baby.rockingBaby = 0;
                    Body.playerBodyAnimator.SetInteger("RockBaby", 0);
                    if (body.EquipItem(babyProp) && body.Drop(false))
                        Finish(0.4f);
                }
                else if (now >= nextBabyRock && body.EquipItem(babyProp))
                {
                    nextBabyRock = now + 2f;
                    babyProp.SetRockingBabyServerRpc(false);
                    baby.rockingBaby = 1;
                    Body.playerBodyAnimator.SetInteger("RockBaby", 1);
                }
                return;
            }
            if (babyProp.isHeld || babyProp.heldByPlayerOnServer || !babyProp.grabbable)
            {
                Finish(0f);
                return;
            }
            Vector3 babyPoint = PickupPoint(babyProp);
            if ((Position - babyPoint).sqrMagnitude <= 2.25f)
            {
                ResetPath();
                if (FocusPickup(babyProp, now) && body.PreparePickup(babyProp))
                    body.Grab(babyProp);
            }
            else
                MoveTo(babyPoint, now);
            return;
        }
        if (order.Kind == TaskKind.Hold || order.Kind == TaskKind.Guard && Aboard || order.Kind == TaskKind.Return && Aboard)
        {
            if (Aboard && Cargo != null)
                Deliver();
            else if (Aboard && (Position - order.Point).sqrMagnitude > 1.44f)
                MoveTo(order.Point, now);
            else
                ResetPath();
            return;
        }
        if (agent.hasPath && !agent.pathPending && !agent.isOnOffMeshLink &&
            (director.Board.Risk(agent.steeringTarget, Inside, now, false, false) >= 0.75f ||
                Blackboard.DogProximityRisk(agent.steeringTarget, Inside) >= 0.75f &&
                Blackboard.DogProximityRisk(agent.steeringTarget, Inside) > Blackboard.DogProximityRisk(Position, Inside) + 0.04f ||
                liftPhase != LiftPhase.Approach && DropAhead()))
        {
            director.Record(this, "hazard", $"avoiding={agent.steeringTarget}");
            RecoverRoute(agent.destination, now);
            return;
        }
        if (Inside && agent.hasPath)
            director.OpenNearbyDoor(this, agent.steeringTarget, now);
        if (order.Kind == TaskKind.Evade)
        {
            director.Outcome(this, 0.5f);
            Assign(new Order(TaskKind.Hold, Position, Inside));
            return;
        }

        if (order.Kind == TaskKind.Transport && Aboard)
        {
            Deliver();
            return;
        }

        if (order.Kind == TaskKind.Stage && !Inside)
        {
            var stagedItem = Cargo;
            if (stagedItem == null)
            {
                Finish(0.45f);
                return;
            }
            if (!director.NearOutsideEntrance(Position, lastExitEntranceId))
            {
                Assign(new Order(TaskKind.Transport, director.Ship));
                director.RequestReplan();
                return;
            }
            if (!body.EquipItem(stagedItem))
                return;
            if (body.Drop(false))
            {
                director.Staged(this, stagedItem);
                preferredEntranceId = lastExitEntranceId;
                nextFacilityEntry = now;
                lastPortalTime = now - 4f;
            }
            return;
        }

        if (order.Kind == TaskKind.Escort && order.Partner != null)
        {
            if (!Inside || !order.Partner.Available || !order.Partner.Inside)
            {
                Assign(new Order(TaskKind.Hold, Position, Inside));
                director.RequestReplan();
                return;
            }
            if ((Position - order.Partner.Position).sqrMagnitude > 9f)
                MoveTo(order.Partner.Position, now);
            else
                ResetPath();
            return;
        }

        if (order.Inside != Inside)
        {
            CrossPortal(order.Inside, now);
            return;
        }

        var item = order.Item;
        if (order.Kind == TaskKind.Scavenge || order.Kind == TaskKind.EquipWeapon)
        {
            if (item == null || Blackboard.HoarderNearItem(item) || LightningHazard.IsTarget(item) || item.isHeld || item.heldByPlayerOnServer || item.isHeldByEnemy)
            {
                Finish(-0.15f);
                return;
            }
            if (crouching && NoisyWhenHeld(item))
            {
                if (failedTargets.Count >= 128)
                    failedTargets.Clear();
                failedTargets[(Vector3Int.FloorToInt(item.transform.position / 3f), Inside)] = now + 20f;
                Finish(0f);
                return;
            }

            Vector3 pickupPoint = PickupPoint(item);
            if ((Position - pickupPoint).sqrMagnitude < 16f &&
                (visibleThreat == null || !Blackboard.Watch(visibleThreat) && !Blackboard.Freeze(visibleThreat)))
                FocusPickup(item, now);
            Vector3 pickupOffset = pickupPoint - Position;
            float pickupHeight = Mathf.Abs(pickupOffset.y);
            pickupOffset.y = 0f;
            if (pickupOffset.sqrMagnitude < 9f && pickupHeight < 2.5f)
            {
                bool visible = PickupVisible(Position + Vector3.up * 1.25f, item, pickupPoint);
                if (!visible)
                {
                    ResetPath();
                    if (TryOpenLootContainer(item, pickupPoint, now))
                        return;
                }
                if (!visible || pickupOffset.sqrMagnitude < 2.25f)
                {
                    ResetPath();
                    if (FocusPickup(item, now))
                        Finish((item is GunAmmo ammo ? CollectAmmo(ammo) : (body.PreparePickup(item) && body.Grab(item))) ? 0.45f : -0.2f);
                    return;
                }
            }
        }

        Vector3 goal = item != null ? item.transform.position : order.Point;
        if (order.Kind == TaskKind.Explore && (Position - goal).sqrMagnitude <= Arrival * Arrival)
        {
            Finish(0.5f);
            return;
        }

        MoveTo(goal, now);
    }

    private static Vector3 PickupPoint(GrabbableObject item)
    {
        var collider = item.GetComponentInChildren<Collider>();
        if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && collider.bounds.size.sqrMagnitude > 0.001f)
            return collider.bounds.center;
        return item.transform.position + Vector3.up * 0.15f;
    }

    private static bool NoisyWhenHeld(GrabbableObject item) => item is AnimatedItem animated &&
        animated.noiseRange > 0f && animated.noiseLoudness >= 0.25f;

    private bool FocusPickup(GrabbableObject item, float now)
    {
        if (visibleThreat != null && (Blackboard.Watch(visibleThreat) || Blackboard.Freeze(visibleThreat)))
            return false;
        scanning = false;
        lookPoint = PickupPoint(item);
        nextLook = now + 0.5f;
        Vector3 direction = lookPoint - Body.gameplayCamera.transform.position;
        return direction.sqrMagnitude < 0.01f || Vector3.Angle(Body.gameplayCamera.transform.forward, direction) <= 25f;
    }

    private bool PickupVisible(Vector3 from, GrabbableObject item, Vector3 target)
    {
        return !Physics.Linecast(from, target, out var hit, director.Board.SightMask, QueryTriggerInteraction.Ignore) ||
            hit.collider != null && (hit.collider.transform == item.transform || hit.collider.transform.IsChildOf(item.transform));
    }

    private bool TryOpenLootContainer(GrabbableObject item, Vector3 target, float now)
    {
        if (visibleThreat != null && (Blackboard.Watch(visibleThreat) || Blackboard.Freeze(visibleThreat)))
            return true;
        if (containerItem != item)
        {
            containerItem = item;
            containerTrigger = null;
            containerTried = false;
            containerWaitUntil = 0f;
            containerAttemptUntil = now + 2f;
            int count = Physics.OverlapSphereNonAlloc(target, 2.5f, nearbyInteractions, ~0, QueryTriggerInteraction.Collide);
            float best = 6.25f;
            for (int index = 0; index < count; index++)
            {
                var collider = nearbyInteractions[index];
                if (collider == null)
                    continue;
                var trigger = collider.GetComponentInParent<InteractTrigger>();
                if (trigger == null || !trigger.interactable || trigger.holdInteraction || trigger.isLadder ||
                    trigger.specialCharacterAnimation || trigger.GetComponentInParent<DoorLock>() != null ||
                    string.IsNullOrEmpty(trigger.hoverTip) ||
                    trigger.hoverTip.IndexOf("open", StringComparison.OrdinalIgnoreCase) < 0 &&
                    trigger.hoverTip.IndexOf("aç", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Vector3 point = collider.ClosestPoint(Body.gameplayCamera.transform.position);
                float distance = (point - Body.gameplayCamera.transform.position).sqrMagnitude;
                if (distance >= best || (point - target).sqrMagnitude > 6.25f ||
                    Physics.Linecast(Body.gameplayCamera.transform.position, point, director.Board.SightMask, QueryTriggerInteraction.Ignore))
                    continue;
                best = distance;
                containerTrigger = trigger;
                containerPoint = point;
            }
        }
        if (now < containerWaitUntil)
            return true;
        if (containerTried || containerTrigger == null || now >= containerAttemptUntil || !containerTrigger.isActiveAndEnabled ||
            containerTrigger.interactCooldown && containerTrigger.currentCooldownValue >= 0f)
            return false;
        scanning = false;
        lookPoint = containerPoint;
        nextLook = now + 0.5f;
        Vector3 direction = lookPoint - Body.gameplayCamera.transform.position;
        if (direction.sqrMagnitude > 0.01f && Vector3.Angle(Body.gameplayCamera.transform.forward, direction) > 25f)
            return true;
        containerTried = true;
        containerWaitUntil = now + 1f;
        containerTrigger.Interact(Body.thisPlayerBody);
        director.Record(this, "loot_container", $"item={item.name} trigger={containerTrigger.name}");
        return true;
    }

    private bool UpdateGrenade(Blackboard.Contact? contact, float now)
    {
        if (primedGrenade != null)
        {
            var grenade = primedGrenade;
            if (grenade.hasExploded || grenade.playerHeldBy != Body)
            {
                primedGrenade = null;
                return false;
            }
            if (grenade.pinPulled && !grenade.inPullingPinAnimation && body.Drop(Aboard, grenadeTarget))
            {
                director.Record(this, "equipment", $"used=stun_grenade target={grenadeTarget}");
                primedGrenade = null;
                nextGrenade = now + 15f;
            }
            return true;
        }
        if (!BotSettings.Defend || Aboard || now < nextGrenade || contact == null || !(contact.Source is EnemyAI enemy) ||
            enemy is BlobAI || !Blackboard.MustFlee(contact, Body) || Blackboard.Freeze(contact) || Blackboard.Watch(contact))
            return false;
        var grenadeItem = body.FindItem<StunGrenadeItem>();
        if (grenadeItem == null || grenadeItem.pinPulled || grenadeItem.hasExploded || grenadeItem.spawnDamagingShockwave ||
            grenadeItem.explodeOnCollision || grenadeItem.dontRequirePullingPin || grenadeItem.chanceToExplode < 100f)
            return false;
        Vector3 target = contact.Sample.Position;
        float distance = (target - Position).sqrMagnitude;
        if (distance < 100f || distance > 196f || Physics.Linecast(Position + Vector3.up, target + Vector3.up,
            director.Board.SightMask, QueryTriggerInteraction.Ignore))
            return false;
        foreach (var player in Body.playersManager.allPlayerScripts)
            if (player != null && player.isPlayerControlled && !player.isPlayerDead &&
                (player.transform.position - target).sqrMagnitude < 100f)
                return false;
        if (!Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var floor, 4f,
            director.Board.SightMask, QueryTriggerInteraction.Ignore) || !body.EquipItem(grenadeItem))
            return false;
        grenadeTarget = floor.point;
        primedGrenade = grenadeItem;
        ActivateItemRpc(grenadeItem, true, true);
        grenadeItem.ItemActivate(true, true);
        return true;
    }

    private bool UpdateBoombox(Blackboard.Contact? contact, float now)
    {
        var box = body.FindItem<BoomboxItem>();
        if (box == null || !box.IsSpawned)
            return false;
        bool slimeNearby = !Aboard && director.Board.SlimeNear(Position, Inside, now);
        bool otherDanger = contact != null && !(contact.Source is BlobAI) && Blackboard.MustFlee(contact, Body) &&
            ((Position - contact.Sample.Position).sqrMagnitude < 64f || contact.Sample.Pursuing == Body);
        bool shouldPlay = slimeNearby && !otherDanger && !frozen && !crouching && box.insertedBattery != null && !box.insertedBattery.empty;
        if (shouldPlay && Body.currentlyHeldObjectServer != box && !body.EquipItem(box))
            shouldPlay = false;
        if (box.isPlayingMusic != shouldPlay)
        {
            ActivateItemRpc(box, shouldPlay, true);
            box.ItemActivate(shouldPlay, true);
        }
        return shouldPlay && contact != null && contact.Source is BlobAI &&
            (Position - contact.Sample.Position).sqrMagnitude < 121f;
    }

    private bool ChargeEquipment(float now)
    {
        var item = chargingItem != null && chargingItem.IsSpawned && chargingItem.isHeld &&
            chargingItem.playerHeldBy == Body ? chargingItem : body.RechargeableItem();
        var charger = director.Charger;
        if (item == null || charger == null || item.insertedBattery == null ||
            now < nextChargeAttempt || !body.EquipItem(item))
        {
            chargingItem = null;
            chargeStartedAt = 0f;
            chargeReadyAt = 0f;
            return false;
        }
        chargingItem = item;
        if (chargeStartedAt == 0f)
            chargeStartedAt = now;
        if (now - chargeStartedAt > 12f)
        {
            chargeStartedAt = 0f;
            chargeReadyAt = 0f;
            chargingItem = null;
            nextChargeAttempt = now + 30f;
            director.Record(this, "equipment", "charge_failed=unreachable");
            director.RequestReplan();
            return false;
        }
        Vector3 point = charger.transform.position;
        if ((Position - point).sqrMagnitude > 4f)
        {
            MoveTo(point, now);
            return true;
        }
        ResetPath();
        if (chargeReadyAt == 0f)
        {
            chargeReadyAt = now + 0.75f;
            charger.PlayChargeItemEffectServerRpc((int)Body.playerClientId);
            return true;
        }
        if (now < chargeReadyAt)
            return true;
        item.insertedBattery = new Battery(false, 1f);
        item.SyncBatteryClientRpc(100);
        chargingItem = null;
        chargeStartedAt = 0f;
        chargeReadyAt = 0f;
        nextChargeAttempt = now + 0.5f;
        director.Record(this, "equipment", $"charged={item.itemProperties.itemName}");
        director.RequestReplan();
        return false;
    }

    private bool StoreUnusedEquipment(float now)
    {
        if (body.FreeScrapSlots() > 0)
            return false;
        foreach (var item in Body.ItemSlots)
        {
            if (item == null || !item.IsSpawned || !item.isHeld || item.itemProperties == null || item.itemProperties.isScrap)
                continue;
            bool unused = item is BoomboxItem && !director.Board.SlimeKnown(now) ||
                director.Charger == null && item.itemProperties.requiresBattery && item.insertedBattery != null && item.insertedBattery.empty;
            if (item is ShotgunItem gun && gun.shellsLoaded == 0 && !gun.isReloading &&
                body.FindAmmo(gun.gunCompatibleAmmoID) == null && !director.HasShipAmmo(gun.gunCompatibleAmmoID))
            {
                bool bagHasAmmo = false;
                var bag = body.FindItem<BeltBagItem>();
                if (bag != null)
                    foreach (var stored in bag.objectsInBag)
                        if (stored is GunAmmo ammo && ammo.ammoType == gun.gunCompatibleAmmoID)
                        {
                            bagHasAmmo = true;
                            break;
                        }
                unused |= !bagHasAmmo;
            }
            if (!unused || item is BeltBagItem || item is StunGrenadeItem grenade && grenade.pinPulled ||
                !body.EquipItem(item) || !body.Drop(true))
                continue;
            director.StoreEquipment(item, now);
            director.Record(this, "equipment", $"stored={item.itemProperties.itemName}");
            director.RequestReplan();
            return true;
        }
        return false;
    }

    private bool CollectAmmo(GunAmmo ammo)
    {
        if (Blackboard.HoarderNearItem(ammo))
            return false;
        var bag = body.FindItem<BeltBagItem>();
        if (bag != null && bag.IsSpawned && bag.objectsInBag.Count < 15 && !bag.tryingAddToBag &&
            ammo.itemProperties != null && !ammo.itemProperties.isScrap && ammo.itemProperties.itemId != 123984 &&
            ammo.itemProperties.itemId != 819501 && !bag.objectsInBag.Contains(ammo))
        {
            bag.TryAddObjectToBagServerRpc(ammo.NetworkObject, (int)Body.playerClientId);
            nextAmmoAction = Time.time + 2f;
            return true;
        }
        return body.PreparePickup() && body.Grab(ammo);
    }

    private void MaintainShotgun(Blackboard.Contact? contact, float now)
    {
        var gun = body.FindItem<ShotgunItem>();
        if (gun == null || !gun.IsSpawned || !gun.IsOwner || gun.isReloading || now < nextAmmoAction ||
            gun.shellsLoaded >= 2 || Body.currentlyHeldObjectServer != null && Body.currentlyHeldObjectServer != gun &&
            Body.currentlyHeldObjectServer.itemProperties.twoHanded || contact != null && Blackboard.MustFlee(contact, Body) &&
            (Position - contact.Sample.Position).sqrMagnitude < 144f)
            return;
        if (pendingBagAmmo != null)
        {
            if (!pendingBagAmmo.IsSpawned || now >= nextAmmoAction + 3f)
                pendingBagAmmo = null;
            else if (!pendingBagAmmo.heldByPlayerOnServer && !pendingBagAmmo.isHeld &&
                body.PreparePickup() && body.Grab(pendingBagAmmo))
            {
                pendingBagAmmo = null;
                nextAmmoAction = now + 0.5f;
            }
            return;
        }
        if (body.FindAmmo(gun.gunCompatibleAmmoID) == null)
        {
            var bag = body.FindItem<BeltBagItem>();
            if (bag == null || bag.currentPlayerChecking != null)
                return;
            foreach (var contained in bag.objectsInBag)
                if (contained is GunAmmo ammo && ammo.IsSpawned && ammo.ammoType == gun.gunCompatibleAmmoID)
                {
                    pendingBagAmmo = ammo;
                    Vector3 floor = ammo.GetItemFloorPosition(Position + Vector3.up);
                    bool inShip = Aboard;
                    var parent = inShip ? Body.playersManager.elevatorTransform : Body.playersManager.propsContainer;
                    bag.RemoveFromBagServerRpc(ammo.NetworkObject, inShip, inShip, parent.InverseTransformPoint(floor),
                        (int)Body.playerClientId, Inside);
                    nextAmmoAction = now + 0.5f;
                    return;
                }
            return;
        }
        if (body.EquipItem(gun))
        {
            gun.ItemInteractLeftRight(true);
            nextAmmoAction = now + 3f;
            director.Record(this, "equipment", "reloading=shotgun");
        }
    }

    private bool TryShoot(EnemyAI enemy, Vector3 position, float now)
    {
        var gun = body.FindItem<ShotgunItem>();
        if (!BotSettings.Defend || gun == null || !gun.IsSpawned || !gun.IsOwner || gun.isReloading ||
            gun.shellsLoaded <= 0 || Body.currentlyHeldObjectServer != null && Body.currentlyHeldObjectServer != gun &&
            Body.currentlyHeldObjectServer.itemProperties.twoHanded || enemy == null || enemy.isEnemyDead || enemy.enemyType == null ||
            !enemy.enemyType.canDie || enemy is SpringManAI || now < nextGunShot)
            return false;
        Vector3 origin = Body.gameplayCamera.transform.position - Body.gameplayCamera.transform.up * 0.45f;
        Vector3 target = position + Vector3.up;
        Vector3 direction = target - origin;
        if (direction.sqrMagnitude > 144f || direction.sqrMagnitude < 4f ||
            Physics.Linecast(origin, target, director.Board.SightMask, QueryTriggerInteraction.Ignore))
            return false;
        foreach (var player in Body.playersManager.allPlayerScripts)
        {
            if (player == null || player == Body || !player.isPlayerControlled || player.isPlayerDead)
                continue;
            Vector3 offset = player.playerCollider != null ? player.playerCollider.ClosestPoint(origin) - origin :
                player.transform.position + Vector3.up - origin;
            if (offset.sqrMagnitude < 900f && Vector3.Angle(direction, offset) < 35f &&
                !Physics.Linecast(origin, origin + offset, director.Board.SightMask, QueryTriggerInteraction.Ignore))
                return false;
        }
        if (!body.EquipItem(gun))
            return false;
        scanning = false;
        lookPoint = target;
        nextLook = now + 1f;
        if (Vector3.Angle(Body.gameplayCamera.transform.forward, direction) > 8f)
            return true;
        if (gun.safetyOn)
        {
            gun.ItemInteractLeftRightOnClient(false);
            return true;
        }
        Body.playerBodyAnimator.SetTrigger("ShootShotgun");
        gun.ShootGunServerRpc(origin, direction.normalized);
        nextGunShot = now + 1.2f;
        director.Record(this, "equipment", "used=shotgun");
        return true;
    }

    private bool TryAttack(Blackboard.Contact? contact, float now)
    {
        if (contact == null || !(contact.Source is EnemyAI enemy) || Blackboard.IgnoreEnemy(enemy) ||
            Blackboard.Freeze(contact) || Blackboard.LookAway(contact) ||
            enemy is PumaAI puma && puma.currentBehaviourStateIndex == 1 && puma.targetPlayer == Body ||
            (enemy is MouthDogAI || enemy is ButlerEnemyAI || enemy is HoarderBugAI || enemy is NutcrackerEnemyAI) && !Blackboard.MustFlee(contact, Body))
            return false;
        if (TryShoot(enemy, contact.Sample.Position, now))
            return true;
        return TryAttackEnemy(enemy, contact.Sample.Position, now);
    }

    private bool TryAttackEnemy(EnemyAI enemy, Vector3 position, float now)
    {
        if (!BotSettings.Defend || !(Body.currentlyHeldObjectServer is Shovel weapon) || enemy == null ||
            enemy.isEnemyDead || enemy.enemyType == null || !enemy.enemyType.canDie || enemy is SpringManAI)
            return false;

        Vector3 target = position + Vector3.up;
        Vector3 camera = Body.gameplayCamera.transform.position;
        Vector3 direction = target - camera;
        if (direction.sqrMagnitude > 3.24f ||
            Physics.Linecast(camera, target, director.Board.SightMask, QueryTriggerInteraction.Ignore))
            return false;

        foreach (var player in Body.playersManager.allPlayerScripts)
        {
            if (player == null || player == Body || !player.isPlayerControlled || player.isPlayerDead)
                continue;
            Vector3 offset = player.transform.position - Position;
            if (offset.sqrMagnitude < 9f && Vector3.Angle(direction, offset + Vector3.up) < 55f)
                return false;
        }

        scanning = false;
        lookPoint = target;
        nextLook = now + 1f;
        if (now >= nextSwing && !weapon.reelingUp && Vector3.Angle(Body.gameplayCamera.transform.forward, direction) < 15f)
        {
            weapon.ItemActivate(true, true);
            swingWeapon = weapon;
            releaseSwingAt = now + 0.4f;
            nextSwing = now + 1f;
        }
        return true;
    }

    private bool GiantFacing(ForestGiantAI giant)
    {
        Vector3 forward = giant.eye != null ? giant.eye.forward : giant.transform.forward;
        Vector3 towardBot = Position - giant.transform.position;
        forward.y = 0f;
        towardBot.y = 0f;
        return forward.sqrMagnitude > 0.01f && towardBot.sqrMagnitude > 0.01f &&
            Vector3.Dot(forward.normalized, towardBot.normalized) > 0.35f;
    }

    private bool TryForestDetour(ForestGiantAI giant, float now)
    {
        if (order.Kind == TaskKind.Hold || order.Kind == TaskKind.Guard || now < nextGiantDetour ||
            !GiantFacing(giant) || (Position - giant.transform.position).sqrMagnitude > 400f ||
            Physics.Linecast(giant.eye != null ? giant.eye.position : giant.transform.position + Vector3.up * 4f, Position + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore))
            return false;
        nextGiantDetour = now + 4f;
        Vector3 forward = giant.eye != null ? giant.eye.forward : giant.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            return false;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = giant.transform.position;
        Vector3 first = center - forward * 9f + right * 8f;
        Vector3 second = center - forward * 9f - right * 8f;
        if ((second - Position).sqrMagnitude < (first - Position).sqrMagnitude)
            (first, second) = (second, first);
        if (TryForestDetourPoint(first, giant, now) || TryForestDetourPoint(second, giant, now))
            return true;
        return false;
    }

    private bool TryForestDetourPoint(Vector3 point, ForestGiantAI giant, float now)
    {
        if (!NavMesh.SamplePosition(point, out var hit, 3f, agent.areaMask) ||
            !agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete ||
            !PathAvoids(candidatePath, giant.transform.position, 6f) || !TryRoute(hit.position, now, false, null, giant))
            return false;
        recoveryGoal = hit.position;
        recoveryUntil = now + 8f;
        return true;
    }

    private bool PathAvoids(NavMeshPath candidate, Vector3 danger, float radius)
    {
        int count = candidate.GetCornersNonAlloc(corners);
        if (count == 0 || count == corners.Length)
            return false;
        for (int i = 1; i < count; i++)
        {
            Vector3 segment = corners[i] - corners[i - 1];
            segment.y = 0f;
            Vector3 offset = danger - corners[i - 1];
            offset.y = 0f;
            float t = segment.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector3.Dot(offset, segment) / segment.sqrMagnitude) : 0f;
            if ((offset - segment * t).sqrMagnitude < radius * radius && offset.sqrMagnitude > radius * radius)
                return false;
        }
        return true;
    }

    private bool TryForestEscape(ForestGiantAI giant, float now)
    {
        Vector3 threat = giant.transform.position;
        if (director.ShipOpen && agent.CalculatePath(director.Ship, candidatePath) &&
            candidatePath.status == NavMeshPathStatus.PathComplete && PathAvoids(candidatePath, threat, 5f) &&
            TryRoute(director.Ship, now, true))
            return true;

        if (selectedPortal == null || selectedPortal.entrancePoint == null)
            selectedPortal = director.Portal(this, now, out portalApproach, fleeFrom: threat);
        if (selectedPortal != null && selectedPortal.exitScript != null &&
            agent.CalculatePath(portalApproach, candidatePath) && candidatePath.status == NavMeshPathStatus.PathComplete &&
            PathAvoids(candidatePath, threat, 5f))
        {
            CrossPortal(true, now);
            return true;
        }
        selectedPortal = null;
        Vector3 eye = giant.eye != null ? giant.eye.position : threat + Vector3.up * 4f;
        Vector3 best = Vector3.zero;
        float bestScore = float.PositiveInfinity;
        int checkedNodes = 0;
        foreach (var node in director.OutdoorNodes)
        {
            float distance = (node - Position).sqrMagnitude;
            if (distance < 16f || distance > 1225f ||
                !Physics.Linecast(eye, node + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore))
                continue;
            if (++checkedNodes > 32)
                break;
            if (!agent.CalculatePath(node, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete ||
                !PathAvoids(candidatePath, threat, 5f))
                continue;
            float score = distance - (node - threat).sqrMagnitude * 0.15f;
            if (score < bestScore)
            {
                best = node;
                bestScore = score;
            }
        }
        return bestScore < float.PositiveInfinity && TryRoute(best, now, true);
    }

    private bool TryThumperTurn(CrawlerAI thumper, float now)
    {
        if (now < thumperTurnUntil && agent.hasPath && (Position - thumperTurnGoal).sqrMagnitude > 2.25f)
            return true;
        if (now < nextThumperTurn)
            return false;
        var portal = director.PlannedEscape(this, now, out var exit);
        if (portal != null && (exit - Position).sqrMagnitude < 36f)
            return false;
        Vector3 threat = thumper.transform.position;
        Vector3 direction = Position - threat;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            return false;
        direction.Normalize();
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        int checkedNodes = 0;
        foreach (var node in director.IndoorNodes)
        {
            Vector3 step = node - Position;
            step.y = 0f;
            float distance = step.magnitude;
            if (distance < 4f || distance > 18f || Mathf.Abs(Vector3.Dot(step / distance, direction)) > 0.8f ||
                (node - threat).sqrMagnitude < 25f)
                continue;
            if (++checkedNodes > 40)
                break;
            if (!agent.CalculatePath(node, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete ||
                !PathAvoids(candidatePath, threat, 3f))
                continue;
            int cornerCount = candidatePath.GetCornersNonAlloc(corners);
            bool covered = Physics.Linecast(threat + Vector3.up, node + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore);
            float score = (covered ? 20f : 0f) + Mathf.Min(cornerCount, 5) * 2f - distance * 0.3f;
            if (portal != null)
                score -= Vector3.Distance(node, exit) * 0.15f;
            if (score > bestScore)
            {
                bestScore = score;
                best = node;
            }
        }
        if (bestScore == float.NegativeInfinity || !TryRoute(best, now, true))
            return false;
        thumperTurnGoal = best;
        thumperTurnUntil = now + 4f;
        nextThumperTurn = now + 7f;
        return true;
    }

    private bool TryEscapeExit(float now, Vector3 threat, bool immediateThreat)
    {
        if (selectedPortal == null || selectedPortal.entrancePoint == null || selectedPortal.exitScript == null)
        {
            selectedPortal = director.PlannedEscape(this, now, out portalApproach) ??
                director.Portal(this, now, out portalApproach, fleeFrom: threat);
            if (selectedPortal != null)
                director.ExitSelected(this);
        }
        if (selectedPortal == null || selectedPortal.exitScript == null)
        {
            director.NoExitAvailable(this, now);
            return false;
        }
        if (immediateThreat && !director.DifferentElevatorFloors(Position, portalApproach) &&
            agent.CalculatePath(portalApproach, candidatePath))
        {
            int count = candidatePath.GetCornersNonAlloc(corners);
            Vector3 towardThreat = threat - Position;
            towardThreat.y = 0f;
            if (count > 1 && towardThreat.sqrMagnitude < 25f)
            {
                Vector3 firstStep = corners[1] - Position;
                firstStep.y = 0f;
                if (firstStep.sqrMagnitude > 0.01f &&
                    Vector3.Dot(firstStep.normalized, towardThreat.normalized) > 0.5f)
                {
                    failedTargets[(Vector3Int.FloorToInt(selectedPortal.entrancePoint.position / 3f), true)] = now + 6f;
                    director.ExitFailed(this, selectedPortal.entranceId, now);
                    selectedPortal = null;
                    return false;
                }
            }
            int index = 1;
            for (; index < count && index < corners.Length; index++)
            {
                Vector3 from = corners[index - 1];
                Vector3 segment = corners[index] - from;
                segment.y = 0f;
                Vector3 toThreat = threat - from;
                toThreat.y = 0f;
                float projection = segment.sqrMagnitude > 0.01f
                    ? Mathf.Clamp01(Vector3.Dot(toThreat, segment) / segment.sqrMagnitude) : 0f;
                Vector3 nearest = from + segment * projection;
                Vector3 threatOffset = threat - nearest;
                threatOffset.y = 0f;
                if ((nearest - Position).sqrMagnitude <= 4f || threatOffset.sqrMagnitude >= 12.25f)
                    continue;
                failedTargets[(Vector3Int.FloorToInt(selectedPortal.entrancePoint.position / 3f), true)] = now + 6f;
                director.ExitFailed(this, selectedPortal.entranceId, now);
                selectedPortal = null;
                return false;
            }
        }
        CrossPortal(false, now);
        return true;
    }

    private void CrossPortal(bool intoFactory, float now)
    {
        if (intoFactory && !Inside)
        {
            if (body.DropPending)
                return;
            var carriedLoot = Cargo;
            if (carriedLoot != null)
            {
                CloseOpeningPortal();
                ResetPath();
                if (body.EquipItem(carriedLoot) && body.Drop(false))
                    director.Staged(this, carriedLoot);
                return;
            }
        }
        if (intoFactory && director.Board.ActiveJester != null)
        {
            selectedPortal = null;
            ResetPath();
            director.RequestReplan();
            return;
        }
        bool retreating = Inside && !intoFactory && now < retreatUntil;
        if (Inside && !intoFactory && Cargo == null && order.Kind != TaskKind.Evade && !retreating &&
            now - enteredAt < 8f && !director.IsLate())
        {
            Assign(new Order(TaskKind.Hold, Position, true));
            director.RequestReplan();
            return;
        }
        if (!retreating && intoFactory != lastPortalEntered && now - lastPortalTime < 4f &&
            order.Kind != TaskKind.Transport && order.Kind != TaskKind.Stage && order.Kind != TaskKind.Return && order.Kind != TaskKind.Evade)
            return;
        if (selectedPortal == null || selectedPortal.entrancePoint == null || selectedPortal.exitScript == null)
        {
            selectedPortal = director.Portal(this, now, out portalApproach, order.Point);
            if (selectedPortal != null && Inside && !intoFactory)
                director.ExitSelected(this);
        }
        var portal = selectedPortal;
        if (portal == null || portal.exitScript == null)
        {
            if (Inside && !intoFactory)
                director.NoExitAvailable(this, now);
            RouteFailed();
            return;
        }

        if (intoFactory && (!director.EntranceAllowed(portal, now) || !CanAttempt(portal.entrancePoint.position, false, now)))
        {
            selectedPortal = null;
            ResetPath();
            director.RequestReplan();
            return;
        }

        Vector3 entrance = portal.entrancePoint.position;
        Vector3 exit = portal.exitScript.entrancePoint.position;
        bool nearEntrance = intoFactory && (Position - entrance).sqrMagnitude <= 36f;
        bool includeRouteFailures = !nearEntrance && Cargo == null && order.Kind != TaskKind.Return &&
            order.Kind != TaskKind.Guard && !director.IsLate();
        ForestGiantAI? chasingGiant = intoFactory ? director.Board.NearbyForestGiant(Position, false) : null;
        if (chasingGiant != null && (chasingGiant.currentBehaviourStateIndex != 1 || chasingGiant.chasingPlayer != Body))
            chasingGiant = null;
        float entryRisk = intoFactory ? director.Board.Risk(entrance, false, now, false, true, true, chasingGiant) : 0f;
        float destinationRisk = director.Board.Risk(exit, intoFactory, now, includeRouteFailures);
        if (destinationRisk >= (intoFactory ? 0.35f : 0.48f) || entryRisk >= 0.35f)
        {
            if (intoFactory && (entryRisk >= 0.35f || director.Board.Risk(exit, true, now, false) >= 0.35f))
                director.MarkUnsafeEntrance(portal.entranceId, now);
            failedTargets[(Vector3Int.FloorToInt(entrance / 3f), Inside)] = now + 6f;
            if (Inside && !intoFactory)
                director.ExitFailed(this, portal.entranceId, now);
            selectedPortal = null;
            ResetPath();
            director.RequestReplan();
            return;
        }

        Vector3 offsetFromEntrance = Position - entrance;
        float heightDifference = Mathf.Abs(offsetFromEntrance.y);
        offsetFromEntrance.y = 0f;
        if (offsetFromEntrance.sqrMagnitude > 12.25f || heightDifference > 3f ||
            (Position - portalApproach).sqrMagnitude > 2.25f)
        {
            if ((Position - portalApproach).sqrMagnitude <= 2.25f &&
                !TryPortalApproach(entrance, out portalApproach))
            {
                RouteFailed();
                return;
            }
            MoveTo(portalApproach, now, fleeing: retreating || chasingGiant != null);
            return;
        }

        if (openingPortal != portal)
        {
            CloseOpeningPortal();
            openingPortal = portal;
            portalReadyAt = now + (portal.entranceId == 0 ? 1.2f : 0.7f);
            ResetPath();
            if (portal.entranceId == 0)
                portal.StartOpeningEntrance();
            return;
        }
        if (now < portalReadyAt)
            return;

        Vector3 landing;
        if (intoFactory)
        {
            if (!NavMesh.SamplePosition(exit, out var hit, 3f, agent.areaMask))
            {
                RouteFailed();
                return;
            }
            landing = hit.position;
        }
        else if (!director.TryOutsideLanding(portal.exitScript, agent.areaMask, now, out landing))
        {
            if (director.Board.Risk(exit, false, now, false) < 0.2f)
                director.ObserveEntrance(portal.entranceId, -0.6f);
            if (now >= nextLandingWarning)
            {
                nextLandingWarning = now + 15f;
                Plugin.Log.LogWarning($"Bot {Body.playerClientId} could not find an outdoor landing connected to entrance {portal.entranceId}.");
            }
            RouteFailed();
            return;
        }
        Vector3 departurePosition = Position;
        if (!agent.Warp(landing))
        {
            RouteFailed();
            return;
        }

        if (portal.entranceId == 0)
            portal.FinishOpeningEntrance(true);
        else
            portal.PlayAudioAtTeleportPositions();
        portal.timeAtLastUse = Time.realtimeSinceStartup;
        portal.exitScript.timeAtLastUse = portal.timeAtLastUse;
        openingPortal = null;

        if (intoFactory && routeLearnActive && (departurePosition - routeLearnGoal).sqrMagnitude <= 25f)
        {
            director.ObserveOutdoorRoute(routeLearnStart, routeLearnGoal, 0.5f);
        }
        if (intoFactory)
            routeLearnActive = false;
        director.ObserveEntrance(portal.entranceId, 0.35f);
        director.ExitCompleted(this, portal.entranceId);

        Body.isInsideFactory = intoFactory;
        if (intoFactory)
        {
            enteredAt = now;
            preferredEntranceId = -1;
            director.PlanEscape(this, now, true);
        }
        else
        {
            lastExitEntranceId = portal.entranceId;
            if (retreating)
                director.MarkUnsafeEntrance(portal.entranceId, now);
            director.LeaveFacility(Body);
            director.BreakTeam(this);
        }
        selectedPortal = null;
        lastPortalTime = now;
        lastPortalEntered = intoFactory;
        if (!intoFactory)
        {
            nextFacilityEntry = now + (retreating || order.Kind == TaskKind.Stage ? 4f : 6f);
            retreatUntil = 0f;
        }
        Plugin.Log.LogInfo($"Bot {Body.playerClientId} {(intoFactory ? "entered" : "left")} the facility through entrance {portal.entranceId}.");
        foreach (var held in Body.ItemSlots)
            if (held != null)
                held.isInFactory = intoFactory;
        if (Body.ItemOnlySlot != null)
            Body.ItemOnlySlot.isInFactory = intoFactory;
        ResetPath();
        nextRoute = 0f;
        director.RequestReplan();
    }

    private void Deliver()
    {
        var item = Cargo;
        if (item == null)
            return;
        Vector3 offset = Position - director.Ship;
        offset.y = 0f;
        if (offset.sqrMagnitude > 1.44f)
        {
            MoveTo(director.Ship, Time.time);
            return;
        }
        if (body.EquipItem(item) && body.Drop(true, director.Ship))
            Finish(Mathf.Clamp(item.scrapValue / 50f, 0.2f, 1f));
    }

    private void EscapeMech(RadMechAI mech, float now)
    {
        if (escapingMech == mech && routed && agent.hasPath && now < nextRoute)
            return;
        escapingMech = mech;
        Vector3 forward = mech.eye != null ? mech.eye.forward : mech.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            return;
        forward.Normalize();
        Vector3 offset = Position - mech.transform.position;
        offset.y = 0f;
        float side = Vector3.Dot(offset, Quaternion.Euler(0f, 90f, 0f) * forward) >= 0f ? 1f : -1f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int index = 0; index < 5; index++)
            {
                float angle = index == 0 ? 90f * side : index == 1 ? -90f * side : index == 2 ? 45f * side : index == 3 ? -45f * side : 0f;
                Vector3 goal = Position + Quaternion.Euler(0f, angle, 0f) * forward * (pass == 0 ? 12f : 6f);
                if (!NavMesh.SamplePosition(goal, out var hit, 2f, agent.areaMask) ||
                    director.Board.EnvironmentalRisk(hit.position, false) >= 0.55f ||
                    pass == 0 && director.Board.InMechSight(hit.position, false))
                    continue;
                if (TryRoute(hit.position, now, true))
                    return;
            }
        }
        ResetPath();
        nextRoute = now + 0.6f;
    }

    private void EscapeNutcracker(NutcrackerEnemyAI nutcracker, float now)
    {
        if (coverNutcracker != nutcracker)
        {
            recoveryUntil = 0f;
            nextRoute = 0f;
            ResetPath();
        }
        Vector3 threat = nutcracker.eye != null ? nutcracker.eye.position : nutcracker.transform.position + Vector3.up;
        if (Physics.Linecast(threat, Position + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore) &&
            Physics.Linecast(threat, Position + Vector3.up * 0.4f, director.Board.SightMask, QueryTriggerInteraction.Ignore))
        {
            coverNutcracker = nutcracker;
            ResetPath();
            return;
        }
        if (coverNutcracker == nutcracker && now < nextCoverSearch && agent.hasPath &&
            (!coverRoute || (Position - coverGoal).sqrMagnitude > 1f &&
            Physics.Linecast(threat, coverGoal + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore)))
            return;

        coverNutcracker = nutcracker;
        nextCoverSearch = now + 1.5f;
        float best = float.PositiveInfinity;
        Vector3 bestPoint = default;
        int checkedNodes = 0;
        var nodes = Inside ? director.IndoorNodes : director.OutdoorNodes;
        foreach (var node in nodes)
        {
            float distance = (node - Position).sqrMagnitude;
            if (distance < 2.25f || distance > 400f || Mathf.Abs(node.y - Position.y) > 3f)
                continue;
            if (++checkedNodes > 64)
                break;
            ConsiderNutcrackerCover(node, threat, distance, ref best, ref bestPoint);
        }
        for (int index = 0; index < 16; index++)
        {
            float angle = index * 45f + (Body.playerClientId % 2UL == 0 ? 0f : 22.5f);
            Vector3 point = Position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (index < 8 ? 4f : 8f);
            ConsiderNutcrackerCover(point, threat, (point - Position).sqrMagnitude, ref best, ref bestPoint);
        }
        if (best < float.PositiveInfinity && TryRoute(bestPoint, now, true))
        {
            coverGoal = bestPoint;
            coverRoute = true;
            return;
        }
        coverRoute = false;
        Escape(nutcracker.transform.position, now);
    }

    private void ConsiderNutcrackerCover(Vector3 point, Vector3 threat, float distance, ref float best, ref Vector3 bestPoint)
    {
        if (distance >= best || !NavMesh.SamplePosition(point, out var hit, 1.5f, agent.areaMask) ||
            Mathf.Abs(hit.position.y - Position.y) > 2f ||
            !Physics.Linecast(threat, hit.position + Vector3.up, director.Board.SightMask, QueryTriggerInteraction.Ignore) ||
            !Physics.Linecast(threat, hit.position + Vector3.up * 0.4f, director.Board.SightMask, QueryTriggerInteraction.Ignore) ||
            !agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete ||
            !PathAvoids(candidatePath, threat, 3f))
            return;
        best = distance;
        bestPoint = hit.position;
    }

    private void EscapeTurret(Turret turret, float now)
    {
        if (turret.aimPoint == null)
            return;
        if (escapingTurret == turret && routed && agent.hasPath && now < nextRoute)
            return;
        escapingTurret = turret;
        Vector3 forward = turret.aimPoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Position - turret.transform.position;
        forward.Normalize();
        Vector3 away = Position - turret.transform.position;
        away.y = 0f;
        float side = Vector3.Dot(away, Quaternion.Euler(0f, 90f, 0f) * forward) >= 0f ? 1f : -1f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int index = 0; index < 5; index++)
            {
                float angle = index == 0 ? 90f * side : index == 1 ? -90f * side : index == 2 ? 135f * side : index == 3 ? -135f * side : 180f;
                Vector3 goal = Position + Quaternion.Euler(0f, angle, 0f) * forward * (pass == 0 ? 8f : 4f);
                if (!NavMesh.SamplePosition(goal, out var hit, 2f, agent.areaMask) ||
                    director.Board.EnvironmentalRisk(hit.position, Inside) >= 0.55f && pass == 0 ||
                    director.Board.EnvironmentalRisk(hit.position, Inside, turret) >= 0.55f)
                    continue;
                if (TryRoute(hit.position, now, true, turret))
                    return;
            }
        }
        ResetPath();
        nextRoute = now + 0.6f;
    }

    private void Escape(Vector3 threat, float now)
    {
        if (routed && (Position - routedGoal).sqrMagnitude > 2f && now < nextRoute)
            return;

        Vector3 away = Position - threat;
        away.y = 0f;
        Vector3 direction = away.sqrMagnitude > 0.01f ? away.normalized : Body.transform.forward;
        int attempt = 0;
        for (; attempt < 5; ++attempt)
        {
            float angle = attempt == 0 ? 0f : (attempt % 2 == 0 ? 1f : -1f) * ((attempt + 1) / 2) * 40f;
            Vector3 goal = Position + Quaternion.Euler(0f, angle, 0f) * direction * 10f;
            if ((goal - threat).sqrMagnitude > away.sqrMagnitude && TryRoute(goal, now, true))
                return;
        }

        ResetPath();
        nextRoute = now + 0.6f;
    }

    private bool TryLeaveHazardArea(float now)
    {
        float initialRisk = director.Board.AreaRisk(Position, Inside);
        if (routed && agent.hasPath && now < nextRoute && director.Board.AreaRisk(routedGoal, Inside) < initialRisk - 0.1f)
            return true;
        float startAngle = UnityEngine.Random.Range(0f, 360f);
        for (int pass = 0; pass < 3; pass++)
        {
            float radius = 6f + pass * 4f;
            for (int index = 0; index < 12; index++)
            {
                Vector3 candidate = Position + Quaternion.Euler(0f, startAngle + index * 30f, 0f) * Vector3.forward * radius;
                if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, agent.areaMask) ||
                    Mathf.Abs(hit.position.y - Position.y) > 2f ||
                    director.Board.AreaRisk(hit.position, Inside) >= initialRisk - 0.1f ||
                    director.Board.StaticEnvironmentalRisk(hit.position, Inside) >= 0.55f)
                    continue;
                if (TryRoute(hit.position, now, true))
                    return true;
            }
        }
        return false;
    }

    private void DropHeavyForEscape(float distance, bool pursued, float now)
    {
        if (Aboard || now < nextEscapeDrop || !Physics.Raycast(Position + Vector3.up, Vector3.down, 2f,
            director.Board.SightMask, QueryTriggerInteraction.Ignore))
            return;
        GrabbableObject? selected = null;
        foreach (var item in Body.ItemSlots)
        {
            if (item == null || !item.IsSpawned || !item.isHeld || item is RagdollGrabbableObject ||
                item.itemProperties == null || !item.itemProperties.isScrap ||
                !TeamDecisionPolicy.ShouldDropForEscape(item.itemProperties.twoHanded, item.itemProperties.weight, distance, pursued))
                continue;
            if (selected == null || item.itemProperties.weight > selected.itemProperties.weight)
                selected = item;
        }
        if (selected == null || !body.DropItem(selected))
            return;
        nextEscapeDrop = now + 8f;
        director.StashEscapeLoot(this, selected, now);
    }

    private bool DropAhead()
    {
        Vector3 direction = agent.steeringTarget - Position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1f || !NavMesh.FindClosestEdge(Position, out var edge, agent.areaMask) ||
            edge.distance > 0.8f || Vector3.Dot(direction.normalized, edge.normal) >= -0.3f)
            return false;
        Vector3 ahead = Position + direction.normalized * 1.3f + Vector3.up;
        return !Physics.Raycast(ahead, Vector3.down, 3.5f, director.Board.SightMask, QueryTriggerInteraction.Ignore);
    }

    private void MoveTo(Vector3 goal, float now, bool elevatorStation = false, bool fleeing = false)
    {
        if (now < recoveryUntil && (Position - recoveryGoal).sqrMagnitude > 0.5f && agent.hasPath)
            return;
        if (recoveryUntil > 0f)
        {
            recoveryUntil = 0f;
            ResetPath();
            nextRoute = 0f;
        }
        if (now < blockedUntil)
            return;
        if (!elevatorStation && Inside && director.DifferentElevatorFloors(Position, goal))
        {
            NavigateElevator(goal, now);
            return;
        }
        if (!elevatorStation && liftPhase == LiftPhase.Approach)
        {
            liftPhase = LiftPhase.None;
            director.ReleaseElevator(this);
        }
        if (waypointActive)
        {
            if ((goal - waypointFinalGoal).sqrMagnitude < 2.25f && now < waypointDeadline && agent.hasPath &&
                (Position - waypointGoal).sqrMagnitude > 1f)
            {
                if ((Position - progressPoint).sqrMagnitude > 0.35f)
                {
                    lastProgress = now;
                    progressPoint = Position;
                }
                else if (now - lastProgress > 2.5f)
                {
                    waypointActive = false;
                    ResetPath();
                    nextRoute = 0f;
                }
                if (waypointActive)
                    return;
            }
            waypointActive = false;
            ResetPath();
            nextRoute = 0f;
        }
        if (pendingLockedDoor != null && pendingLockedDoor.isLocked &&
            (Body.currentlyHeldObjectServer is KeyItem || body.FindItem<LockPicker>() != null) &&
            (Position - pendingLockedDoor.transform.position).sqrMagnitude <= 6.25f)
        {
            if (director.OpenNearbyDoor(this, pendingLockedDoor.transform.position, now))
            {
                pendingLockedDoor = null;
                ResetPath();
                nextRoute = now + 0.6f;
            }
            else if (!pendingLockedDoor.isPickingLock && body.FindItem<LockPicker>() is LockPicker picker &&
                body.PlaceLockPicker(picker, pendingLockedDoor))
            {
                director.Record(this, "equipment", "used=lockpicker");
                RouteFailed();
            }
            else if (now - pendingDoorSince > 5f && director.Board.Risk(pendingLockedDoor.transform.position, Inside, now) < 0.35f)
                RouteFailed();
            return;
        }
        if (routed && (goal - routedGoal).sqrMagnitude < 2.25f && pendingLockedDoor == null && !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance) &&
            (Position - goal).sqrMagnitude > 2.25f)
        {
            RecoverRoute(goal, now);
            return;
        }
        if (routed && (goal - routedGoal).sqrMagnitude < 2.25f && agent.hasPath && !agent.isOnOffMeshLink)
        {
            if ((Position - progressPoint).sqrMagnitude > 0.35f)
            {
                lastProgress = now;
                progressPoint = Position;
            }
            else if (now - lastProgress > 2.5f)
            {
                RecoverRoute(goal, now);
            }
            return;
        }

        if (now < nextRoute || !fleeing && !elevatorStation && TryRouteWaypoint(goal, now) || TryRoute(goal, now, fleeing))
            return;
        if (director.OpenNearbyDoor(this, goal, now, fleeing))
            nextRoute = now + 0.6f;
        else
            RecoverRoute(goal, now);
    }

    private bool TryRouteWaypoint(Vector3 goal, float now)
    {
        if ((Position - goal).sqrMagnitude < 81f || order.Kind == TaskKind.Escort || order.Kind == TaskKind.Evade ||
            !NavMesh.SamplePosition(goal, out var destination, 2f, agent.areaMask) ||
            !agent.CalculatePath(destination.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete)
            return false;

        int count = candidatePath.GetCornersNonAlloc(corners);
        if (count < 2 || count == corners.Length)
            return false;
        Vector3 firstCorner = corners[1];
        if (count >= 3)
        {
            Vector3 corner = firstCorner;
            Vector3 incoming = corner - Position;
            Vector3 outgoing = corners[2] - corner;
            incoming.y = 0f;
            outgoing.y = 0f;
            if (incoming.sqrMagnitude >= 1f && incoming.sqrMagnitude <= 100f && outgoing.sqrMagnitude >= 1f &&
                Vector3.Dot(incoming.normalized, outgoing.normalized) < 0.85f &&
                (!hasPreviousCorner || (corner - previousCorner).sqrMagnitude > 4f) &&
                NavMesh.FindClosestEdge(corner, out var edge, agent.areaMask) && edge.distance < 0.8f)
            {
                previousCorner = corner;
                hasPreviousCorner = true;
                Vector3 rounded = corner + edge.normal * 0.75f + outgoing.normalized * 0.8f;
                if (StartWaypoint(rounded, goal, now))
                    return true;
            }
        }

        if (laneUsed)
            return false;
        laneUsed = true;
        Vector3 segment = firstCorner - Position;
        segment.y = 0f;
        float length = segment.magnitude;
        if (length < 5f)
            return false;
        int lane = MovementPolicy.Lane(Body.playerClientId);
        if (lane == 0)
            return false;
        Vector3 direction = segment / length;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3 staggered = Position + direction * Mathf.Min(length * 0.6f, 7f) + side * (lane * 0.65f);
        return StartWaypoint(staggered, goal, now);
    }

    private bool StartWaypoint(Vector3 candidate, Vector3 goal, float now)
    {
        if (!NavMesh.SamplePosition(candidate, out var hit, 0.8f, agent.areaMask) ||
            Mathf.Abs(hit.position.y - Position.y) > 1.5f || (hit.position - Position).sqrMagnitude < 2.25f ||
            !NavMesh.FindClosestEdge(hit.position, out var edge, agent.areaMask) || edge.distance < 0.4f ||
            !NavMesh.CalculatePath(hit.position, goal, agent.areaMask, candidatePath) ||
            candidatePath.status != NavMeshPathStatus.PathComplete || !TryRoute(hit.position, now, false))
            return false;
        waypointActive = true;
        agent.autoBraking = false;
        waypointGoal = hit.position;
        waypointFinalGoal = goal;
        waypointDeadline = now + 8f;
        return true;
    }

    private void RecoverRoute(Vector3 goal, float now)
    {
        if (routeRecoveryAttempts++ < 3)
        {
            if (Inside && director.OpenNearbyDoor(this, goal, now))
            {
                ResetPath();
                nextRoute = now + 0.6f;
                return;
            }
            ResetPath();
            Vector3 direction = goal - Position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                direction = Body.transform.forward;
            float angle = routeRecoveryAttempts == 1 ? 90f : routeRecoveryAttempts == 2 ? -90f : 180f;
            Vector3 candidate = Position + Quaternion.Euler(0f, angle, 0f) * direction.normalized * 2f;
            if (TryRoute(candidate, now, false))
            {
                recoveryGoal = candidate;
                recoveryUntil = now + 3f;
                director.Record(this, "route_recovery", $"attempt={routeRecoveryAttempts} goal={goal}");
                return;
            }
        }
        recoveryUntil = 0f;
        RouteFailed();
    }

    private bool TryRoute(Vector3 goal, float now, bool fleeing, Turret? ignoredTurret = null, EnemyAI? ignoredEnemy = null)
    {
        if (!NavMesh.SamplePosition(goal, out var hit, 2f, agent.areaMask) ||
            !agent.CalculatePath(hit.position, path))
            return false;

        int count = path.GetCornersNonAlloc(corners);
        if (count == corners.Length || count == 0)
            return false;
        DoorLock? blockedDoor = null;
        if (path.status != NavMeshPathStatus.PathComplete)
        {
            if (!(Body.currentlyHeldObjectServer is KeyItem) && body.FindItem<LockPicker>() == null)
                return false;
            blockedDoor = director.LockedDoorNear(corners[count - 1]);
            if (blockedDoor == null)
                return false;
            if ((corners[count - 1] - blockedDoor.transform.position).sqrMagnitude > 6.25f)
            {
                if (!NavMesh.SamplePosition(blockedDoor.transform.position, out var approach, 2.4f, agent.areaMask) ||
                    !agent.CalculatePath(approach.position, path) || path.status != NavMeshPathStatus.PathComplete)
                    return false;
                count = path.GetCornersNonAlloc(corners);
                if (count == 0 || count == corners.Length)
                    return false;
            }
        }

        if (Inside && !fleeing)
        {
            SpikeRoofTrap? spike = null;
            for (int index = 1; index < count && spike == null; index++)
                spike = director.Board.Spikes.Crossing(corners[index - 1], corners[index]);
            if (spike != null)
            {
                if (avoidedSpike != spike || now - spikeAvoidLastSeen > 12f)
                {
                    avoidedSpike = spike;
                    spikeAvoidUntil = now + 2f;
                }
                spikeAvoidLastSeen = now;
                if (now < spikeAvoidUntil)
                {
                    if (now >= nextSpikeDetour)
                    {
                        nextSpikeDetour = now + 2f;
                        if (TrySpikeDetour(spike, hit.position, now))
                            return true;
                    }
                    return false;
                }
            }
        }

        {
            bool includeRouteFailures = Cargo == null && order.Kind != TaskKind.Explore && order.Kind != TaskKind.Return &&
                order.Kind != TaskKind.Guard && !director.IsLate();
            bool initiallyInMechSight = fleeing && director.Board.InMechSight(Position, Inside);
            float initialDogRisk = Blackboard.DogProximityRisk(Position, Inside);
            bool leavingDog = !fleeing && initialDogRisk / BotSettings.RiskTolerance >= 0.55f;
            float previousAreaRisk = director.Board.AreaRisk(Position, Inside);
            bool leavingArea = previousAreaRisk >= 0.5f;
            int corner = 1;
            for (; corner < count; ++corner)
            {
                Vector3 segment = corners[corner] - corners[corner - 1];
                int samples = Mathf.Max(1, Mathf.CeilToInt(segment.magnitude / 2f));
                for (int sample = 1; sample <= samples; sample++)
                {
                    Vector3 point = corners[corner - 1] + segment * (sample / (float)samples);
                    float areaRisk = director.Board.AreaRisk(point, Inside);
                    bool cadaverEscape = fleeing && AreaHazardPolicy.CanEscapeThroughCadaver(
                        director.Board.CadaverRisk(point, Inside), director.Board.AreaRiskWithoutCadaver(point, Inside));
                    if (!AreaHazardPolicy.CanTraverse(areaRisk, previousAreaRisk, leavingArea) && !cadaverEscape)
                        return false;
                    if (areaRisk < 0.5f)
                        leavingArea = false;
                    previousAreaRisk = areaRisk;
                    float risk = fleeing ? director.Board.StaticEnvironmentalRisk(point, Inside, ignoredTurret) :
                        director.Board.Risk(point, Inside, now, includeRouteFailures, true, false, ignoredEnemy);
                    if (risk >= 0.55f)
                    {
                        float dogRisk = Blackboard.DogProximityRisk(point, Inside);
                        if (!leavingDog || dogRisk > initialDogRisk + 0.04f ||
                            director.Board.Risk(point, Inside, now, includeRouteFailures, false, false, ignoredEnemy) >= 0.55f)
                            return false;
                    }
                    else
                        leavingDog = false;
                    if (fleeing)
                    {
                        bool inMechSight = director.Board.InMechSight(point, Inside);
                        if (inMechSight && !initiallyInMechSight)
                            return false;
                        if (!inMechSight)
                            initiallyInMechSight = false;
                    }
                }
            }
        }

        if (!agent.SetPath(path))
            return false;

        if (!Inside && !Aboard && !fleeing && (Position - goal).sqrMagnitude >= 64f &&
            (!routeLearnActive || (routeLearnGoal - goal).sqrMagnitude >= 16f))
        {
            routeLearnStart = Position;
            routeLearnGoal = goal;
            routeLearnActive = true;
        }

        if (pendingLockedDoor != blockedDoor)
            pendingDoorSince = now;
        pendingLockedDoor = blockedDoor;
        routed = true;
        routedGoal = goal;
        nextRoute = now + 0.8f;
        lastProgress = now;
        progressPoint = Position;
        return true;
    }

    private bool TrySpikeDetour(SpikeRoofTrap trap, Vector3 goal, float now)
    {
        Vector3 center = trap.laserEye != null ? trap.laserEye.position : trap.transform.position;
        center.y = Position.y;
        for (int index = 0; index < 8; index++)
        {
            Vector3 candidate = center + Quaternion.Euler(0f, index * 45f, 0f) * Vector3.forward * 5f;
            if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, agent.areaMask) ||
                Mathf.Abs(hit.position.y - Position.y) > 2.5f || (hit.position - Position).sqrMagnitude < 4f ||
                director.Board.Risk(hit.position, true, now, false) >= 0.35f ||
                !agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete ||
                !SpikeFreePath(candidatePath) || !NavMesh.CalculatePath(hit.position, goal, agent.areaMask, spikeDetourPath) ||
                spikeDetourPath.status != NavMeshPathStatus.PathComplete || !SpikeFreePath(spikeDetourPath))
                continue;
            if (!TryRoute(hit.position, now, false))
                continue;
            recoveryGoal = hit.position;
            recoveryUntil = now + 10f;
            director.Record(this, "spike_detour", $"target={hit.position}");
            return true;
        }
        return false;
    }

    private bool SpikeFreePath(NavMeshPath candidate)
    {
        int count = candidate.GetCornersNonAlloc(spikeCorners);
        if (count == 0 || count == spikeCorners.Length)
            return false;
        for (int index = 1; index < count; index++)
            if (director.Board.Spikes.Crossing(spikeCorners[index - 1], spikeCorners[index]) != null)
                return false;
        return true;
    }

    private void RouteFailed()
    {
        if (Time.time < blockedUntil)
            return;
        int failedExit = selectedPortal != null ? selectedPortal.entranceId : -1;
        Vector3 failedGoal = selectedPortal != null ? portalApproach : order.Point;
        string failureCause = liftPhase != LiftPhase.None ? "elevator" :
            pendingLockedDoor != null || director.DoorNearRoute(Position, failedGoal) ? "door" :
            !agent.enabled || !agent.isOnNavMesh ? "navmesh_unavailable" :
            agent.pathStatus == NavMeshPathStatus.PathPartial ? "navmesh_partial" : "stalled";
        float retryDelay = director.RecordRouteFailure(this, failedExit, failedGoal, failureCause, Time.time);
        if (!Inside && routeLearnActive)
        {
            if (director.Board.Risk(Position, false, Time.time, false) < 0.2f &&
                director.Board.Risk(routeLearnGoal, false, Time.time, false) < 0.2f)
                director.ObserveOutdoorRoute(routeLearnStart, routeLearnGoal, -0.5f);
            routeLearnActive = false;
        }
        if (order.Kind == TaskKind.Explore && order.Inside && !Inside)
            Plugin.Log.LogWarning($"Bot {Body.playerClientId} could not reach facility entrance target {order.Point}; trying another route.");
        if (selectedPortal != null && selectedPortal.entrancePoint != null)
        {
            selectedPortal = null;
        }
        if (liftPhase != LiftPhase.None)
            AbortElevator();
        blockedUntil = Time.time + 0.4f;
        if (order.Kind == TaskKind.Explore || order.Kind == TaskKind.Scavenge)
        {
            if (failedTargets.Count >= 128)
                failedTargets.Clear();
            failedTargets[(Vector3Int.FloorToInt(order.Point / 3f), order.Inside)] = Time.time + retryDelay;
        }
        recoveryUntil = 0f;
        routeRecoveryAttempts = 0;
        blockedUntil = Time.time + 0.3f;
        nextRoute = blockedUntil;
        ResetPath();
        director.Record(this, "route_retry", $"task={order.Kind} inside={Inside} retry={retryDelay:F0}s target={failedGoal}");
        director.RequestReplan();
    }

    private static Vector3 ElevatorLanding(MineshaftElevatorController lift, bool goingDown)
    {
        if (goingDown)
            return lift.elevatorTopPoint.position;
        return lift.elevatorBottomPointDoor != null ? lift.elevatorBottomPointDoor.position : lift.elevatorBottomPoint.position;
    }

    private bool TryElevatorLanding(Vector3 landing, out Vector3 approach)
    {
        approach = default;
        float bestScore = float.PositiveInfinity;
        int index = 0;
        for (; index < 17; index++)
        {
            float radius = index == 0 ? 0f : index <= 8 ? 1.25f : 2.25f;
            float angle = (index - 1) * Mathf.PI / 4f;
            Vector3 candidate = landing + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (!NavMesh.SamplePosition(candidate, out var hit, 1f, agent.areaMask) ||
                Mathf.Abs(hit.position.y - landing.y) > 2f || (hit.position - landing).sqrMagnitude > 12.25f ||
                !agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete)
                continue;
            float score = (hit.position - landing).sqrMagnitude + (hit.position - Position).sqrMagnitude * 0.005f;
            if (score >= bestScore)
                continue;
            bestScore = score;
            approach = hit.position;
        }
        return !float.IsPositiveInfinity(bestScore);
    }

    private void NavigateElevator(Vector3 goal, float now)
    {
        var lift = director.Elevator;
        if (lift == null || lift.elevatorTopPoint == null || lift.elevatorBottomPoint == null || lift.elevatorInsidePoint == null || lift.elevatorBounds == null)
        {
            RouteFailed();
            return;
        }

        if (!director.ReserveElevator(this, now))
        {
            ResetPath();
            return;
        }

        if (liftPhase == LiftPhase.None)
        {
            liftPhase = LiftPhase.Approach;
            liftGoingDown = Mathf.Abs(goal.y - lift.elevatorBottomPoint.position.y) < Mathf.Abs(goal.y - lift.elevatorTopPoint.position.y);
            liftDeadline = now + 75f;
            Vector3 landing = ElevatorLanding(lift, liftGoingDown);
            if (!TryElevatorLanding(landing, out liftApproach))
            {
                Plugin.Log.LogWarning($"Bot {Body.playerClientId} cannot reach the mineshaft elevator landing near {landing}.");
                AbortElevator();
                RouteFailed();
                return;
            }
            Plugin.Log.LogInfo($"Bot {Body.playerClientId} is approaching the mineshaft elevator ({(liftGoingDown ? "down" : "up")}).");
        }
        if (now > liftDeadline)
        {
            AbortElevator();
            RouteFailed();
            return;
        }

        if ((Position - liftApproach).sqrMagnitude > 2.25f)
        {
            MoveTo(liftApproach, now, elevatorStation: true);
            return;
        }

        ResetPath();
        if (!lift.elevatorFinishedMoving)
            return;
        bool atDeparture = lift.elevatorMovingDown != liftGoingDown;
        if (!atDeparture)
        {
            director.UseElevatorControl(this, true, !liftGoingDown, now);
            return;
        }
        if (!lift.elevatorDoorOpen)
            return;

        liftPhase = LiftPhase.Board;
        liftProgressPosition = Body.transform.position;
        liftProgressTime = now;
        agent.enabled = false;
    }

    private void TickElevator(float now)
    {
        var lift = director.Elevator;
        if (lift == null || lift.elevatorInsidePoint == null || lift.elevatorBounds == null || now > liftDeadline)
        {
            AbortElevator();
            RouteFailed();
            return;
        }

        if (liftPhase == LiftPhase.Board)
        {
            if (!lift.elevatorFinishedMoving || !lift.elevatorDoorOpen)
            {
                AbortElevator();
                return;
            }
            Vector3 toCenter = Body.transform.position - lift.elevatorInsidePoint.position;
            toCenter.y = 0f;
            if (lift.elevatorBounds.bounds.Contains(Body.transform.position + Vector3.up * 0.5f) && toCenter.sqrMagnitude < 2.25f)
            {
                liftOffset = lift.elevatorInsidePoint.InverseTransformPoint(Body.transform.position);
                liftPhase = LiftPhase.Ride;
                Plugin.Log.LogInfo($"Bot {Body.playerClientId} boarded the mineshaft elevator.");
                director.UseElevatorControl(this, false, false, now);
                return;
            }
            MoveInsideElevator(lift.elevatorInsidePoint.position, now);
            return;
        }

        if (liftPhase == LiftPhase.Ride)
        {
            Vector3 position = lift.elevatorInsidePoint.TransformPoint(liftOffset);
            agent.transform.position = position;
            body.Pose(position, Body.transform.rotation, false, true, false);
            if (lift.elevatorFinishedMoving && lift.elevatorMovingDown == liftGoingDown && lift.elevatorDoorOpen)
            {
                liftPhase = LiftPhase.Exit;
                liftProgressPosition = position;
                liftProgressTime = now;
            }
            else if (lift.elevatorFinishedMoving && lift.elevatorMovingDown != liftGoingDown)
                director.UseElevatorControl(this, false, false, now);
            return;
        }

        if (!lift.elevatorFinishedMoving || !lift.elevatorDoorOpen)
            return;
        Vector3 exit = ElevatorLanding(lift, !liftGoingDown);
        Vector3 toExit = Body.transform.position - exit;
        toExit.y = 0f;
        if (toExit.sqrMagnitude <= 2.25f && Mathf.Abs(Body.transform.position.y - exit.y) < 3f &&
            NavMesh.SamplePosition(Body.transform.position, out var hit, 1.25f, agent.areaMask))
        {
            agent.transform.position = hit.position;
            agent.enabled = true;
            if (!agent.isOnNavMesh)
            {
                agent.enabled = false;
                MoveInsideElevator(exit, now);
                return;
            }
            body.Pose(hit.position, Body.transform.rotation, false, true, false);
            liftPhase = LiftPhase.None;
            director.ReleaseElevator(this);
            director.RequestReplan();
            Plugin.Log.LogInfo($"Bot {Body.playerClientId} exited the mineshaft elevator.");
            return;
        }
        MoveInsideElevator(exit, now);
    }

    private void MoveInsideElevator(Vector3 target, float now)
    {
        Vector3 direction = target - Body.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f && Body.thisController != null && Body.thisController.enabled)
        {
            Vector3 step = direction.normalized * Mathf.Min(direction.magnitude, speed * Time.deltaTime);
            Body.thisController.Move(step);
            agent.transform.position = Body.transform.position;
            body.Pose(Body.transform.position, Quaternion.RotateTowards(Body.transform.rotation, Quaternion.LookRotation(direction), aimSpeed * Time.deltaTime), true, true, false, false, step);
        }

        if ((Body.transform.position - liftProgressPosition).sqrMagnitude > 0.16f)
        {
            liftProgressPosition = Body.transform.position;
            liftProgressTime = now;
        }
        else if (now - liftProgressTime > 5f)
        {
            AbortElevator();
            RouteFailed();
        }
    }

    private void AbortElevator()
    {
        if (liftPhase != LiftPhase.None)
            Plugin.Log.LogWarning($"Bot {Body.playerClientId} could not complete elevator travel ({liftPhase}).");
        if (!agent.enabled && NavMesh.SamplePosition(Body.transform.position, out var hit, 2f, agent.areaMask))
        {
            agent.transform.position = hit.position;
            agent.enabled = true;
        }
        liftPhase = LiftPhase.None;
        director.ReleaseElevator(this);
    }

    private void Finish(float reward)
    {
        if (!Inside && routeLearnActive && reward > 0f && (Position - routeLearnGoal).sqrMagnitude <= 9f)
        {
            director.ObserveOutdoorRoute(routeLearnStart, routeLearnGoal, Mathf.Clamp01(reward));
            routeLearnActive = false;
        }
        director.Outcome(this, reward);
        Assign(new Order(TaskKind.Hold, Aboard ? director.Ship : Position, Inside));
    }

    private void ResetPath()
    {
        waypointActive = false;
        agent.autoBraking = true;
        routed = false;
        pendingLockedDoor = null;
        if (agent.isOnNavMesh && agent.hasPath && !agent.isOnOffMeshLink)
            agent.ResetPath();
    }

    private void CloseOpeningPortal()
    {
        if (openingPortal != null && openingPortal.entranceId == 0)
            openingPortal.FinishOpeningEntrance(true);
        openingPortal = null;
    }

    internal bool HiddenEnter(Vector3 position, float now)
    {
        if (Inside || !agent.enabled || !agent.isOnNavMesh || !agent.Warp(position))
            return false;
        CloseOpeningPortal();
        Body.isInsideFactory = true;
        enteredAt = now;
        lastPortalTime = now;
        lastPortalEntered = true;
        preferredEntranceId = -1;
        foreach (var held in Body.ItemSlots)
            if (held != null)
                held.isInFactory = true;
        if (Body.ItemOnlySlot != null)
            Body.ItemOnlySlot.isInFactory = true;
        body.Pose(position, Body.transform.rotation, false, true, false);
        Assign(new Order(TaskKind.Hold, position, true));
        director.PlanEscape(this, now, true);
        director.RequestReplan();
        return true;
    }

    internal void Teleported(Vector3 position)
    {
        director.LeaveFacility(Body);
        if (liftPhase != LiftPhase.None)
            AbortElevator();
        if (agent.enabled)
            agent.enabled = false;
        agent.transform.position = position;
        if (NavMesh.SamplePosition(position, out var hit, 2f, agent.areaMask) &&
            Mathf.Abs(hit.position.y - position.y) < 1.5f)
        {
            agent.transform.position = hit.position;
            agent.enabled = true;
        }
        retreatUntil = 0f;
        nextFacilityEntry = Time.time + 10f;
        director.BreakTeam(this);
        Assign(new Order(TaskKind.Hold, Position, Body.isInsideFactory));
        director.RequestReplan();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        CloseOpeningPortal();
        director.ReleaseElevator(this);
        if (Body != null && Body.IsOwnedByServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress)
            body.DropAll();
        director.Remove(this, Died);
        body.Dispose();
        UnityEngine.Object.Destroy(agent.gameObject);
    }
}
