using System;
using System.Collections.Generic;
using HarmonyLib;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalFakeClient;

public interface IThreatAdapter
{
    bool TryObserve(Component source, out ThreatSample sample);
}

public readonly struct ThreatSample
{
    public readonly Vector3 Position;
    public readonly bool Outside;
    public readonly bool Alive;
    public readonly PlayerControllerB? Pursuing;

    public ThreatSample(Vector3 position, bool outside, bool alive, PlayerControllerB? pursuing = null)
    {
        Position = position;
        Outside = outside;
        Alive = alive;
        Pursuing = pursuing;
    }
}

public static class ThreatSources
{
    internal static readonly Dictionary<Component, IThreatAdapter> Registered = new Dictionary<Component, IThreatAdapter>();

    // Call on the Unity thread and unregister when the source is removed.
    public static void Register(Component source, IThreatAdapter adapter)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        Registered[source] = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public static void Unregister(Component source) => Registered.Remove(source);
}

internal enum TaskKind
{
    Hold, Explore, Scavenge, Transport, Escort, Return, Guard, Evade, Stage, EquipWeapon, ShipControl, CareBaby, AssistPeeper,
    OpenDelivery, CollectDelivery, DeliverDelivery
}

internal readonly struct Order
{
    internal readonly TaskKind Kind;
    internal readonly Vector3 Point;
    internal readonly bool Inside;
    internal readonly GrabbableObject? Item;
    internal readonly IBot? Partner;

    internal Order(TaskKind kind, Vector3 point, bool inside = false, GrabbableObject? item = null, IBot? partner = null)
    {
        Kind = kind;
        Point = point;
        Inside = inside;
        Item = item;
        Partner = partner;
    }
}

internal interface IBot
{
    PlayerControllerB Body { get; }
    Vector3 Position { get; }
    bool Aboard { get; }
    bool Inside { get; }
    bool Available { get; }
    bool CanGuard { get; }
    bool ElevatorTransit { get; }
    GrabbableObject? Cargo { get; }
    Order Order { get; }
    float AssignedAt { get; }
    float EnteredAt { get; }
    int PreferredEntranceId { get; }
    bool CanAttempt(Vector3 point, bool inside, float now);
    bool CanReach(Vector3 point, bool inside);
    bool TryPortalApproach(Vector3 point, out Vector3 approach);
    bool CanEnterFacility(float now);
    void Assign(Order order);
}

internal sealed class Blackboard
{
    internal sealed class Contact
    {
        internal readonly Component Source;
        internal readonly string Key;
        internal ThreatSample Sample;
        internal float Seen;
        internal float Speed;
        internal float Severity;

        internal Contact(Component source)
        {
            Source = source;
            Key = "enemy:" + (source.GetType().FullName ?? source.GetType().Name);
        }
    }

    private static readonly AccessTools.FieldRef<NutcrackerEnemyAI, bool> inspecting = AccessTools.FieldRefAccess<NutcrackerEnemyAI, bool>("isInspecting");
    private static readonly AccessTools.FieldRef<HoarderBugAI, bool> hoarderChasing = AccessTools.FieldRefAccess<HoarderBugAI, bool>("inChase");
    private static readonly AccessTools.FieldRef<NutcrackerEnemyAI, int> nutcrackerTarget = AccessTools.FieldRefAccess<NutcrackerEnemyAI, int>("lastPlayerSeenMoving");

    internal static bool IgnoreEnemy(Component source)
    {
        return PeeperCompatibility.AttachedPlayer(source) != null || source is CaveDwellerAI baby && baby.currentBehaviourStateIndex == 0 ||
            source is BaboonBirdAI || source is FlowerSnakeEnemy || source is ButlerBeesEnemyAI || source is DoublewingAI ||
            source is DocileLocustBeesAI || source is PufferAI || source is CadaverGrowthAI || source is SteamValveHazard || source is SteamValveFixInteraction ||
            source != null && !(source is EnemyAI) && source.GetComponentInParent<SteamValveHazard>() != null;
    }

    private static bool AreaHazard(Component source) => source is BlobAI || source is CadaverBloomAI;

    private static bool CoordinationThreat(Component source, ThreatSample sample)
    {
        bool ready = !(source is EnemyAI enemy) || enemy.ventAnimationFinished;
        bool neutralOrHazard = NeutralHoarder(source) || AreaHazard(source) ||
            source is Landmine || source is Turret || source is SpikeRoofTrap;
        return CoordinationPolicy.RelevantThreat(sample.Outside, ready, neutralOrHazard);
    }

    internal static bool MustFlee(Contact contact, PlayerControllerB bot)
    {
        if (IgnoreEnemy(contact.Source) || AreaHazard(contact.Source) || contact.Source is SpringManAI)
            return false;
        if (contact.Source is ForestGiantAI giant)
            return giant.currentBehaviourStateIndex == 1 && giant.chasingPlayer == bot;
        if (contact.Source is HoarderBugAI hoarder)
            return HoarderHostile(hoarder);
        if (contact.Source is ButlerEnemyAI butler)
            return butler.targetPlayer == bot;
        if (contact.Source is NutcrackerEnemyAI nutcracker)
            return NutcrackerTarget(nutcracker) == bot;
        if (contact.Source is FlowermanAI bracken)
            return bracken.isInAngerMode || bracken.currentBehaviourStateIndex == 2;
        if (contact.Source is MouthDogAI dog)
            return DogPursues(dog, bot);
        if (contact.Source is PumaAI puma && puma.currentBehaviourStateIndex == 1 && puma.targetPlayer == bot)
            return false;
        return true;
    }

    internal static bool Freeze(Contact contact) => contact.Source is NutcrackerEnemyAI nutcracker && inspecting(nutcracker);

    internal static bool Watch(Contact contact) => contact.Source is SpringManAI ||
        contact.Source is FlowermanAI bracken && !bracken.isInAngerMode && bracken.currentBehaviourStateIndex == 0;

    internal static bool LookAway(Contact contact) => contact.Source is FlowermanAI bracken &&
        !bracken.isInAngerMode && bracken.currentBehaviourStateIndex == 1;

    private static readonly HashSet<EnemyAI> enemies = new HashSet<EnemyAI>();
    internal static IEnumerable<EnemyAI> Enemies => enemies;
    private static readonly HashSet<RedLocustBees> beeSwarms = new HashSet<RedLocustBees>();
    private static readonly HashSet<HoarderBugAI> hoarders = new HashSet<HoarderBugAI>();
    private static readonly HashSet<MouthDogAI> dogs = new HashSet<MouthDogAI>();
    private static readonly HashSet<SandWormAI> worms = new HashSet<SandWormAI>();
    private static readonly HashSet<NutcrackerEnemyAI> nutcrackers = new HashSet<NutcrackerEnemyAI>();
    private static readonly HashSet<GrabbableObject> items = new HashSet<GrabbableObject>();
    private static readonly HashSet<GrabbableObject> humanDrops = new HashSet<GrabbableObject>();
    internal static IEnumerable<GrabbableObject> Items => items;
    internal static bool HumanDropped(GrabbableObject item) => humanDrops.Contains(item);
    internal static void ForgetHumanDrop(GrabbableObject item) => humanDrops.Remove(item);
    internal static void HumanDrop(GrabbableObject item)
    {
        if (humanDrops.Count >= 2048)
            humanDrops.Clear();
        humanDrops.Add(item);
        Register(item);
    }
    internal static void Register(EnemyAI enemy)
    {
        if (!enemies.Add(enemy))
            return;
        if (enemy is RedLocustBees bees)
            beeSwarms.Add(bees);
        if (enemy is HoarderBugAI bug)
            hoarders.Add(bug);
        if (enemy is MouthDogAI dog)
            dogs.Add(dog);
        if (enemy is SandWormAI worm)
            worms.Add(worm);
        if (enemy is NutcrackerEnemyAI nutcracker)
            nutcrackers.Add(nutcracker);
    }

    internal static void Register(GrabbableObject item) => items.Add(item);
    internal static void Remove(EnemyAI enemy)
    {
        enemies.Remove(enemy);
        if (enemy is RedLocustBees bees)
            beeSwarms.Remove(bees);
        if (enemy is HoarderBugAI bug)
            hoarders.Remove(bug);
        if (enemy is MouthDogAI dog)
            dogs.Remove(dog);
        if (enemy is SandWormAI worm)
            worms.Remove(worm);
        if (enemy is NutcrackerEnemyAI nutcracker)
            nutcrackers.Remove(nutcracker);
    }

    internal static PlayerControllerB? NutcrackerTarget(NutcrackerEnemyAI nutcracker)
    {
        var round = StartOfRound.Instance;
        if (round == null || round.allPlayerScripts == null)
            return null;
        int index = nutcrackerTarget(nutcracker);
        if (!NutcrackerPolicy.HasTarget(nutcracker.currentBehaviourStateIndex, index, round.allPlayerScripts.Length))
            return null;
        return round.allPlayerScripts[index];
    }

    internal static NutcrackerEnemyAI? TargetingNutcracker(PlayerControllerB bot, bool inside)
    {
        NutcrackerEnemyAI? nearest = null;
        float best = float.PositiveInfinity;
        foreach (var nutcracker in nutcrackers)
        {
            if (nutcracker == null || nutcracker.isEnemyDead || !nutcracker.isActiveAndEnabled ||
                nutcracker.isOutside == inside || NutcrackerTarget(nutcracker) != bot)
                continue;
            float distance = (nutcracker.transform.position - bot.transform.position).sqrMagnitude;
            if (distance >= best)
                continue;
            nearest = nutcracker;
            best = distance;
        }
        return nearest;
    }

    internal static SandWormAI? ThreateningWorm(PlayerControllerB bot, Vector3 position)
    {
        SandWormAI? closest = null;
        float best = float.PositiveInfinity;
        foreach (var worm in worms)
        {
            if (worm == null || worm.isEnemyDead || !worm.isActiveAndEnabled || !worm.isOutside)
                continue;
            Vector3 offset = worm.transform.position - position;
            float height = Mathf.Abs(offset.y);
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            bool targeting = worm.currentBehaviourStateIndex == 1 && worm.targetPlayer == bot;
            if (!MovementPolicy.WormThreat(targeting, worm.inEmergingState || worm.emerged, distance, height) || distance >= best)
                continue;
            closest = worm;
            best = distance;
        }
        return closest;
    }

    private static bool DogPursues(MouthDogAI dog, PlayerControllerB bot)
    {
        bool hasDestination = dog.agent != null && dog.agent.isActiveAndEnabled && dog.agent.isOnNavMesh && dog.agent.hasPath;
        float distance = hasDestination ? (dog.agent!.destination - bot.transform.position).sqrMagnitude : float.PositiveInfinity;
        if (DogStealthPolicy.IsPursuing(dog.targetPlayer == bot, dog.currentBehaviourStateIndex, hasDestination, distance))
            return true;
        Vector3 towardBot = bot.transform.position - dog.transform.position;
        towardBot.y = 0f;
        Vector3 forward = dog.transform.forward;
        forward.y = 0f;
        return dog.currentBehaviourStateIndex == 3 && towardBot.sqrMagnitude < 144f && towardBot.sqrMagnitude > 0.01f &&
            forward.sqrMagnitude > 0.01f && Vector3.Dot(forward.normalized, towardBot.normalized) > 0.8f;
    }

    internal bool DogNearby(Vector3 position, bool inside, PlayerControllerB bot, bool alreadyAlert, out MouthDogAI? pursuer)
    {
        bool nearby = false;
        pursuer = null;
        foreach (var dog in dogs)
        {
            if (dog == null || dog.isEnemyDead || !dog.isActiveAndEnabled || !dog.ventAnimationFinished || dog.isOutside == inside ||
                !DogStealthPolicy.WithinAlertRange((dog.transform.position - position).sqrMagnitude, alreadyAlert))
                continue;
            nearby = true;
            if (DogPursues(dog, bot) && (pursuer == null ||
                (dog.transform.position - position).sqrMagnitude < (pursuer.transform.position - position).sqrMagnitude))
                pursuer = dog;
        }
        return nearby;
    }

    internal bool DogNearPosition(Vector3 position, bool inside)
    {
        foreach (var dog in dogs)
            if (dog != null && !dog.isEnemyDead && dog.isActiveAndEnabled && dog.ventAnimationFinished &&
                dog.isOutside != inside && DogStealthPolicy.WithinAlertRange((dog.transform.position - position).sqrMagnitude, true))
                return true;
        return false;
    }

    internal bool ThreatNearShip(Vector3 position, float now)
    {
        foreach (var contact in contacts.Values)
        {
            if (!contact.Sample.Alive || !contact.Sample.Outside || now - contact.Seen > 8f ||
                NeutralHoarder(contact.Source) || IgnoreEnemy(contact.Source) || AreaHazard(contact.Source) ||
                contact.Source is EnemyAI enemy && (enemy.isEnemyDead || enemy.isInsidePlayerShip) ||
                (contact.Sample.Position - position).sqrMagnitude > 256f)
                continue;
            return true;
        }
        return false;
    }

    internal static float DogProximityRisk(Vector3 point, bool inside)
    {
        float risk = 0f;
        foreach (var dog in dogs)
        {
            if (dog == null || dog.isEnemyDead || !dog.isActiveAndEnabled || !dog.ventAnimationFinished || dog.isOutside == inside)
                continue;
            float distance = Vector3.Distance(point, dog.transform.position);
            if (distance < 6f)
                risk = Mathf.Max(risk, Mathf.Clamp01((6f - distance) / 4f));
        }
        return risk;
    }
    internal static void Remove(GrabbableObject item)
    {
        items.Remove(item);
        humanDrops.Remove(item);
    }

    private static bool HoarderHostile(HoarderBugAI bug) => HoarderPolicy.IsHostile(bug.currentBehaviourStateIndex, hoarderChasing(bug));

    private static bool NeutralHoarder(Component source) => source is HoarderBugAI bug && !HoarderHostile(bug);

    internal static bool GuardedBeeHive(GrabbableObject item)
    {
        if (item == null || item.isInFactory || beeSwarms.Count == 0)
            return false;
        bool isHive = false;
        foreach (var swarm in beeSwarms)
        {
            if (swarm != null && swarm.hive == item)
            {
                isHive = true;
                break;
            }
        }
        if (!isHive)
            return false;
        Vector3 position = item.transform.position;
        foreach (var swarm in beeSwarms)
        {
            if (swarm == null || swarm.isEnemyDead || !swarm.isActiveAndEnabled || !swarm.isOutside)
                continue;
            Vector3 offset = swarm.transform.position - position;
            float height = Mathf.Abs(offset.y);
            offset.y = 0f;
            if (BeeHivePolicy.Guarding(true, swarm.daytimeEnemyLeaving, offset.sqrMagnitude, height, swarm.defenseDistance))
                return true;
        }
        return false;
    }

    internal static bool HoarderNearItem(GrabbableObject item)
    {
        if (item == null)
            return false;
        Vector3 position = item.transform.position;
        foreach (var bug in hoarders)
        {
            if (bug == null || bug.isEnemyDead || !bug.isActiveAndEnabled || bug.isOutside == item.isInFactory)
                continue;
            Vector3 offset = position - bug.transform.position;
            float height = Mathf.Abs(offset.y);
            offset.y = 0f;
            if (HoarderPolicy.ProtectsLoot(offset.sqrMagnitude, height))
                return true;
        }
        return false;
    }

    private readonly Dictionary<Component, Contact> contacts = new Dictionary<Component, Contact>();
    private readonly List<Component> expired = new List<Component>();
    private readonly Dictionary<Vector3Int, (float Value, float Time)> routeRisk = new Dictionary<Vector3Int, (float, float)>();
    private readonly Dictionary<(Vector3Int Cell, bool Inside), float> deaths = new Dictionary<(Vector3Int, bool), float>();
    private readonly Dictionary<GrabbableObject, Vector3> knownLoot = new Dictionary<GrabbableObject, Vector3>();
    private readonly List<GrabbableObject> seenItems = new List<GrabbableObject>();
    private readonly int sightMask;
    private readonly EnvironmentalHazards hazards;
    private readonly AreaHazards areaHazards = new AreaHazards();
    internal SpikeTrapSafety Spikes { get; } = new SpikeTrapSafety();
    private float lastCoordinationThreat = float.NegativeInfinity;
    private Component? coordinationSource;
    private float nextItemDiscovery;
    internal bool Coordinated { get; private set; }
    internal bool OutdoorThreat { get; private set; }
    internal JesterAI? ActiveJester { get; private set; }
    internal int SightMask => sightMask;

    internal Blackboard(int sightMask)
    {
        this.sightMask = sightMask;
        hazards = new EnvironmentalHazards(sightMask);
        enemies.RemoveWhere(enemy => enemy == null);
        beeSwarms.RemoveWhere(bees => bees == null);
        hoarders.RemoveWhere(bug => bug == null);
        dogs.RemoveWhere(dog => dog == null);
        worms.RemoveWhere(worm => worm == null);
        nutcrackers.RemoveWhere(nutcracker => nutcracker == null);
        items.RemoveWhere(item => item == null);
        foreach (var enemy in UnityEngine.Object.FindObjectsOfType<EnemyAI>())
            Register(enemy);
        foreach (var item in UnityEngine.Object.FindObjectsOfType<GrabbableObject>())
            items.Add(item);
    }

    internal bool IndoorEnemyKnown(float now)
    {
        foreach (var contact in contacts.Values)
            if (!contact.Sample.Outside && contact.Sample.Alive && now - contact.Seen < 12f &&
                contact.Source is EnemyAI enemy && !enemy.isEnemyDead && enemy.isActiveAndEnabled &&
                enemy.ventAnimationFinished && !NeutralHoarder(enemy) && !IgnoreEnemy(enemy) && !AreaHazard(enemy))
                return true;
        return false;
    }

    internal bool SlimeKnown(float now)
    {
        foreach (var contact in contacts.Values)
            if (contact.Source is BlobAI && contact.Sample.Alive && now - contact.Seen < 80f)
                return true;
        return false;
    }

    internal bool SlimeNear(Vector3 position, bool inside, float now)
    {
        foreach (var contact in contacts.Values)
            if (contact.Source is BlobAI && contact.Sample.Alive && contact.Sample.Outside != inside &&
                now - contact.Seen < 8f && (contact.Sample.Position - position).sqrMagnitude < 121f &&
                !Physics.Linecast(contact.Sample.Position, position, sightMask, QueryTriggerInteraction.Ignore))
                return true;
        return false;
    }

    internal void Refresh(List<IBot> bots, Learning learning, float now, PlayerControllerB? radarFocus)
    {
        Spikes.Refresh(now);
        if (now >= nextItemDiscovery)
        {
            nextItemDiscovery = now + 3f;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<GrabbableObject>())
                Register(item);
        }
        hazards.Refresh(bots, now);
        if (RoundManager.Instance != null)
        {
            foreach (var enemy in RoundManager.Instance.SpawnedEnemies)
            {
                if (enemy != null)
                    Register(enemy);
            }
        }

        areaHazards.Refresh(enemies, now);

        ActiveJester = null;
        foreach (var enemy in enemies)
        {
            if (enemy is JesterAI jester && !jester.isEnemyDead && jester.isActiveAndEnabled && !jester.isOutside &&
                (jester.currentBehaviourStateIndex == 1 || jester.currentBehaviourStateIndex == 2))
                ActiveJester = jester;
            if (enemy != null && !ThreatSources.Registered.ContainsKey(enemy))
                Observe(enemy, new ThreatSample(enemy.transform.position, enemy.isOutside, !enemy.isEnemyDead && enemy.isActiveAndEnabled,
                    enemy is NutcrackerEnemyAI nutcracker ? NutcrackerTarget(nutcracker) :
                    enemy is ForestGiantAI giant && giant.currentBehaviourStateIndex == 1 ? giant.chasingPlayer :
                    enemy is HoarderBugAI hoarder && HoarderHostile(hoarder) && hoarder.targetPlayer == null ? hoarder.angryAtPlayer : enemy.targetPlayer), bots, learning, now, radarFocus);
        }

        expired.Clear();
        foreach (var pair in ThreatSources.Registered)
        {
            if (pair.Key == null)
            {
                expired.Add(pair.Key!);
                continue;
            }

            try
            {
                if (pair.Value.TryObserve(pair.Key, out var sample))
                    Observe(pair.Key, sample, bots, learning, now, radarFocus);
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Disabling threat adapter for {pair.Key.GetType().Name}: {error.Message}");
                expired.Add(pair.Key!);
            }
        }
        foreach (var source in expired)
            ThreatSources.Registered.Remove(source);

        OutdoorThreat = false;
        expired.Clear();
        foreach (var pair in contacts)
        {
            if (pair.Key == null || pair.Key is EnemyAI enemy && (enemy.isEnemyDead || !enemy.isActiveAndEnabled) ||
                !pair.Value.Sample.Alive || now - pair.Value.Seen > 80f)
                expired.Add(pair.Key!);
            else if (pair.Value.Sample.Outside && !NeutralHoarder(pair.Key) && !AreaHazard(pair.Key))
                OutdoorThreat = true;
        }
        foreach (var source in expired)
            contacts.Remove(source);
        bool wasCoordinated = Coordinated;
        Coordinated = CoordinationPolicy.ShouldCoordinate(now, lastCoordinationThreat);
        if (!wasCoordinated && Coordinated && coordinationSource != null)
            Plugin.Log.LogInfo($"Indoor coordination started after observing {coordinationSource.GetType().Name} at {coordinationSource.transform.position}.");
    }

    private void Observe(Component source, ThreatSample sample, List<IBot> bots, Learning learning, float now, PlayerControllerB? radarFocus)
    {
        if (IgnoreEnemy(source) || source is RadMechAI mech &&
            (!mech.isActiveAndEnabled || !mech.ventAnimationFinished || mech.isEnemyDead))
        {
            contacts.Remove(source);
            return;
        }
        contacts.TryGetValue(source, out var contact);
        if (!sample.Alive)
        {
            if (contact != null)
                contact.Sample = sample;
            return;
        }

        if (contact == null && !source.gameObject.activeInHierarchy)
            return;

        if (radarFocus != null && radarFocus.isPlayerControlled && !radarFocus.isPlayerDead &&
            radarFocus.isInsideFactory != sample.Outside &&
            (radarFocus.transform.position - sample.Position).sqrMagnitude < 144f)
        {
            if (contact == null)
            {
                contact = new Contact(source);
                contacts.Add(source, contact);
            }
            contact.Seen = now;
            contact.Sample = sample;
            contact.Severity = Mathf.Clamp(0.48f + (sample.Pursuing != null ? 0.25f : 0f) - learning.Estimate(contact.Key) * 0.25f, 0.35f, 1f);
            Vector3 radarEye = radarFocus.gameplayCamera != null ? radarFocus.gameplayCamera.transform.position : radarFocus.transform.position + Vector3.up;
            if (CoordinationThreat(source, sample) && !Physics.Linecast(radarEye, sample.Position + Vector3.up * 0.8f,
                sightMask, QueryTriggerInteraction.Ignore))
            {
                lastCoordinationThreat = now;
                coordinationSource = source;
            }
        }

        foreach (var bot in bots)
        {
            if (!bot.Available || bot.Inside == sample.Outside)
                continue;

            Vector3 eye = bot.Position + Vector3.up * 1.4f;
            Vector3 target = sample.Position + Vector3.up * 0.8f;
            Vector3 direction = target - eye;
            bool targetedBotNearby = direction.sqrMagnitude <= 400f && (sample.Pursuing == bot.Body ||
                source is HoarderBugAI hoarder && HoarderHostile(hoarder) && hoarder.angryAtPlayer == bot.Body);
            bool nearbyFlyingHoarder = source is HoarderBugAI flying && HoarderHostile(flying) && direction.sqrMagnitude <= 144f &&
                !Physics.Linecast(eye, target, sightMask, QueryTriggerInteraction.Ignore);
            if (!targetedBotNearby && !nearbyFlyingHoarder && (direction.sqrMagnitude > 60f * 60f || direction.sqrMagnitude > 16f &&
                Vector3.Angle(bot.Body.gameplayCamera.transform.forward, direction) > BotSettings.ViewAngle * 0.5f ||
                Physics.Linecast(eye, target, sightMask, QueryTriggerInteraction.Ignore)))
                continue;

            if (contact == null)
            {
                contact = new Contact(source);
                contacts.Add(source, contact);
            }
            else
            {
                float elapsed = Mathf.Max(0.1f, now - contact.Seen);
                contact.Speed = Mathf.Lerp(contact.Speed, Mathf.Min(14f, (sample.Position - contact.Sample.Position).magnitude / elapsed), 0.35f);
            }

            contact.Seen = now;
            contact.Sample = sample;
            contact.Severity = Mathf.Clamp(0.48f + contact.Speed * 0.025f + (sample.Pursuing != null ? 0.25f : 0f) - learning.Estimate(contact.Key) * 0.25f, 0.35f, 1f);
            if (CoordinationThreat(source, sample))
            {
                lastCoordinationThreat = now;
                coordinationSource = source;
            }
            break;
        }
    }

    internal ForestGiantAI? NearbyForestGiant(Vector3 position, bool inside)
    {
        ForestGiantAI? closest = null;
        float best = 900f;
        if (inside)
            return null;
        foreach (var enemy in enemies)
        {
            if (!(enemy is ForestGiantAI giant) || giant.isEnemyDead || !giant.isActiveAndEnabled || !giant.isOutside)
                continue;
            float distance = (giant.transform.position - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                closest = giant;
            }
        }
        return closest;
    }

    internal static FlowerSnakeEnemy? AttachedTulip(PlayerControllerB player)
    {
        foreach (var enemy in enemies)
            if (enemy is FlowerSnakeEnemy snake && !snake.isEnemyDead && snake.isActiveAndEnabled && snake.clingingToPlayer == player)
                return snake;
        return null;
    }

    internal static FlowerSnakeEnemy? MainTulip(PlayerControllerB player)
    {
        foreach (var enemy in enemies)
            if (enemy is FlowerSnakeEnemy snake && !snake.isEnemyDead && snake.isActiveAndEnabled &&
                snake.clingingToPlayer == player && snake.clingPosition == 4)
                return snake;
        return null;
    }

    internal FlowerSnakeEnemy? NearbyTulipSnake(Vector3 position, bool inside)
    {
        FlowerSnakeEnemy? closest = null;
        float bestDistance = 9f;
        foreach (var source in enemies)
        {
            if (!(source is FlowerSnakeEnemy snake) || snake.isEnemyDead || !snake.isActiveAndEnabled ||
                snake.isOutside == inside)
                continue;
            float distance = (snake.transform.position - position).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            closest = snake;
            bestDistance = distance;
        }
        return closest;
    }

    internal PumaAI? TargetingPuma(PlayerControllerB bot, Vector3 position, bool inside)
    {
        foreach (var enemy in enemies)
            if (enemy is PumaAI puma && puma.isActiveAndEnabled && !puma.isEnemyDead && puma.isOutside != inside &&
                puma.targetPlayer == bot && (puma.transform.position - position).sqrMagnitude <= 1225f)
                return puma;
        return null;
    }

    internal Contact? Nearby(Vector3 position, bool inside, float now)
    {
        Contact? closest = null;
        float score = 0f;
        foreach (var contact in contacts.Values)
        {
            if (!contact.Sample.Alive || contact.Sample.Outside == inside || now - contact.Seen > 8f || NeutralHoarder(contact.Source) || AreaHazard(contact.Source))
                continue;
            float distance = (position - contact.Sample.Position).sqrMagnitude;
            float danger = contact.Severity * (1f - distance / (24f * 24f));
            if (danger <= score)
                continue;
            score = danger;
            closest = contact;
        }
        return closest;
    }

    internal Contact? NearbyFleeing(Vector3 position, bool inside, PlayerControllerB bot, float now)
    {
        Contact? closest = null;
        float score = 0f;
        foreach (var contact in contacts.Values)
        {
            if (!contact.Sample.Alive || contact.Sample.Outside == inside || now - contact.Seen > 8f || !MustFlee(contact, bot))
                continue;
            float distance = (position - contact.Sample.Position).sqrMagnitude;
            float danger = contact.Severity * (1f - distance / (24f * 24f));
            if (danger <= score)
                continue;
            score = danger;
            closest = contact;
        }
        return closest;
    }

    internal bool InMechSight(Vector3 position, bool inside) => MechSeeing(position, inside) != null;

    internal RadMechAI? MechSeeing(Vector3 position, bool inside)
    {
        if (inside)
            return null;
        foreach (var contact in contacts.Values)
        {
            if (!(contact.Source is RadMechAI mech) || !mech.isActiveAndEnabled || !mech.ventAnimationFinished ||
                mech.isEnemyDead || !contact.Sample.Alive)
                continue;
            Vector3 forward = mech.eye != null ? mech.eye.forward : mech.transform.forward;
            Vector3 offset = position - mech.transform.position;
            offset.y = 0f;
            forward.y = 0f;
            if (offset.sqrMagnitude < 1600f && forward.sqrMagnitude > 0.01f &&
                Vector3.Angle(forward, offset) < Mathf.Max(40f, mech.fov) &&
                !Physics.Linecast(mech.transform.position + Vector3.up * 0.7f, position + Vector3.up, sightMask, QueryTriggerInteraction.Ignore))
                return mech;
        }
        return null;
    }

    internal Contact? Tactical(Vector3 position, bool inside, PlayerControllerB bot, float now)
    {
        Contact? best = null;
        float bestScore = 0f;
        foreach (var contact in contacts.Values)
        {
            if (!contact.Sample.Alive || contact.Sample.Outside == inside || now - contact.Seen > 8f || NeutralHoarder(contact.Source) || AreaHazard(contact.Source))
                continue;
            float distance = (position - contact.Sample.Position).magnitude;
            if (distance > 24f)
                continue;
            float score = (Freeze(contact) ? 100f : Watch(contact) ? 80f : MustFlee(contact, bot) ? 60f : 40f) - distance;
            if (score > bestScore)
            {
                best = contact;
                bestScore = score;
            }
        }
        return best;
    }

    internal float EnvironmentalRisk(Vector3 position, bool inside, Turret? ignored = null) => Mathf.Max(hazards.Risk(position, inside, ignored), areaHazards.Risk(position, inside));
    internal float AreaRisk(Vector3 position, bool inside) => areaHazards.Risk(position, inside);
    internal float AreaRiskWithoutCadaver(Vector3 position, bool inside) => areaHazards.RiskWithoutCadaver(position, inside);
    internal float CadaverRisk(Vector3 position, bool inside) => areaHazards.CadaverRisk(position, inside);
    internal float StaticEnvironmentalRisk(Vector3 position, bool inside, Turret? ignored = null) => hazards.Risk(position, inside, ignored);
    internal Turret? DangerousTurret(Vector3 position, bool inside, PlayerControllerB bot) => hazards.DangerousTurret(position, inside, bot);

    internal float Risk(Vector3 position, bool inside, float now, bool includeRouteFailures = true, bool includeDogs = true, bool includeAreas = true, EnemyAI? ignoredEnemy = null, bool includeDeaths = true, bool includeStatic = true)
    {
        float risk = InMechSight(position, inside) ? 1f : includeStatic ? hazards.Risk(position, inside) : 0f;
        if (includeDogs)
            risk = Mathf.Max(risk, DogProximityRisk(position, inside));
        if (includeAreas)
            risk = Mathf.Max(risk, areaHazards.Risk(position, inside));
        if (includeRouteFailures && routeRisk.TryGetValue(Cell(position), out var failure))
            risk = Mathf.Max(risk, failure.Value * Mathf.Clamp01(1f - (now - failure.Time) / 90f));
        if (includeDeaths)
            risk = Mathf.Max(risk, DeathRisk(position, inside, now));
        risk = Mathf.Max(risk, EnemyRisk(position, inside, now, ignoredEnemy));
        return risk / BotSettings.RiskTolerance;
    }

    internal float EnemyRisk(Vector3 position, bool inside, float now, EnemyAI? ignoredEnemy = null)
    {
        float risk = 0f;
        foreach (var contact in contacts.Values)
        {
            if (!contact.Sample.Alive || contact.Sample.Outside == inside || contact.Source == ignoredEnemy)
                continue;
            if (NeutralHoarder(contact.Source) || AreaHazard(contact.Source) || contact.Source is SpringManAI || contact.Source is ButlerEnemyAI butler &&
                (butler.targetPlayer == null || butler.currentBehaviourStateIndex != 2) || contact.Source is MouthDogAI dog && dog.currentBehaviourStateIndex < 2 ||
                contact.Source is FlowermanAI bracken && !bracken.isInAngerMode && bracken.currentBehaviourStateIndex != 2 ||
                contact.Source is NutcrackerEnemyAI)
                continue;
            float proximity = 1f - (position - contact.Sample.Position).magnitude / 24f;
            float confidence = Mathf.Clamp01(1f - (now - contact.Seen) / 80f);
            risk = Mathf.Max(risk, contact.Severity * proximity * confidence);
        }
        return risk;
    }

    internal void RouteFailed(Vector3 point)
    {
        if (routeRisk.Count >= 2048)
            routeRisk.Clear();
        var cell = Cell(point);
        float previous = 0f;
        if (routeRisk.TryGetValue(cell, out var failure))
            previous = failure.Value * Mathf.Clamp01(1f - (Time.time - failure.Time) / 90f);
        routeRisk[cell] = (Mathf.Min(0.85f, previous + 0.23f), Time.time);
    }

    internal void RouteRecovered(Vector3 point) => routeRisk.Remove(Cell(point));

    internal void RecordDeath(Vector3 position, bool inside, float now)
    {
        if (deaths.Count >= 256)
            deaths.Clear();
        deaths[(Cell(position), inside)] = now;
    }

    internal float DeathRisk(Vector3 position, bool inside, float now)
    {
        return deaths.TryGetValue((Cell(position), inside), out float time)
            ? 0.72f * Mathf.Clamp01(1f - (now - time) / 180f) : 0f;
    }

    internal Vector3 KnownLootPosition(GrabbableObject item)
    {
        if (item == null)
            return Vector3.zero;
        return knownLoot.TryGetValue(item, out var position) ? position : item.transform.position;
    }

    internal void ForgetLoot(GrabbableObject item) => knownLoot.Remove(item);

    internal List<GrabbableObject> ScavengableItems(List<IBot> bots)
    {
        seenItems.Clear();
        foreach (var item in Items)
        {
            if (item == null)
                continue;
            if (!item.isActiveAndEnabled || !item.IsSpawned || item.itemProperties == null || item.isHeld ||
                item.heldByPlayerOnServer || item.isHeldByEnemy || item.isInShipRoom || !item.grabbable)
            {
                knownLoot.Remove(item);
                continue;
            }
            if (!(item is RagdollGrabbableObject body && body.ragdoll != null && BotSettings.RecoverBodies ||
                item.itemProperties.isScrap && item.scrapValue > 0 && BotSettings.CollectItems) || GuardedBeeHive(item))
                continue;
            if (!item.isInFactory || item is RagdollGrabbableObject)
            {
                knownLoot[item] = item.transform.position;
                seenItems.Add(item);
                continue;
            }
            Vector3 point = item.transform.position + Vector3.up * 0.2f;
            foreach (var bot in bots)
            {
                if (!bot.Available || !bot.Inside || (bot.Position - point).sqrMagnitude > 900f)
                    continue;
                Vector3 eye = bot.Body.gameplayCamera.transform.position;
                if ((bot.Position - point).sqrMagnitude <= 16f ||
                    !Physics.Linecast(eye, point, out var hit, SightMask, QueryTriggerInteraction.Ignore) ||
                    hit.collider != null && (hit.collider.transform == item.transform || hit.collider.transform.IsChildOf(item.transform)))
                {
                    knownLoot[item] = item.transform.position;
                    break;
                }
            }
            if (knownLoot.ContainsKey(item))
                seenItems.Add(item);
        }
        return seenItems;
    }

    internal bool ShipSupport(List<IBot> bots) => hazards.Support(bots);

    internal void Dispose()
    {
        hazards.Dispose();
        areaHazards.Dispose();
    }

    private static Vector3Int Cell(Vector3 point) => Vector3Int.FloorToInt(point / 6f);
}
