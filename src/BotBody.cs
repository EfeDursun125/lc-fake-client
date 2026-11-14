using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class BotBody : IDisposable
{
    private static readonly Dictionary<PlayerControllerB, BotBody> active = new Dictionary<PlayerControllerB, BotBody>();
    private static readonly int Walking = Animator.StringToHash("Walking");
    private static readonly int Sprinting = Animator.StringToHash("Sprinting");
    private static readonly int Sideways = Animator.StringToHash("Sideways");
    private static readonly int Crouching = Animator.StringToHash("crouching");
    private static readonly int EmoteNumber = Animator.StringToHash("emoteNumber");
    private static readonly int AnimationSpeed = Animator.StringToHash("animationSpeed");
    private static readonly AccessTools.FieldRef<Terminal, InteractTrigger> TerminalTrigger =
        AccessTools.FieldRefAccess<Terminal, InteractTrigger>("terminalTrigger");
    private static readonly int GrabParameter = Animator.StringToHash("Grab");
    private static readonly int GrabValidated = Animator.StringToHash("GrabValidated");
    private static readonly int CancelHolding = Animator.StringToHash("cancelHolding");
    private static readonly Action<PlayerControllerB, NetworkObjectReference> GrabRpc =
        AccessTools.MethodDelegate<Action<PlayerControllerB, NetworkObjectReference>>(AccessTools.Method(typeof(PlayerControllerB), "GrabObjectServerRpc"));
    private static readonly Action<PlayerControllerB, NetworkObjectReference, bool, bool, Vector3, int> DropRpc =
        AccessTools.MethodDelegate<Action<PlayerControllerB, NetworkObjectReference, bool, bool, Vector3, int>>(AccessTools.Method(typeof(PlayerControllerB), "ThrowObjectServerRpc"));
    private static readonly Action<PlayerControllerB, int, GrabbableObject> SwitchSlot =
        AccessTools.MethodDelegate<Action<PlayerControllerB, int, GrabbableObject>>(AccessTools.Method(typeof(PlayerControllerB), "SwitchToItemSlot"));
    private static readonly Action<PlayerControllerB, int> SwitchSlotRpc =
        AccessTools.MethodDelegate<Action<PlayerControllerB, int>>(AccessTools.Method(typeof(PlayerControllerB), "SwitchToSlotServerRpc"));
    private static readonly Action<PlayerControllerB, NetworkObjectReference, NetworkObjectReference, Vector3, bool> PlaceRpc =
        AccessTools.MethodDelegate<Action<PlayerControllerB, NetworkObjectReference, NetworkObjectReference, Vector3, bool>>(
            AccessTools.Method(typeof(PlayerControllerB), "PlaceObjectServerRpc"));
    private static readonly Action<PlayerControllerB> DespawnLocal =
        AccessTools.MethodDelegate<Action<PlayerControllerB>>(AccessTools.Method(typeof(PlayerControllerB), "DespawnHeldObjectOnClient"));
    private static readonly Action<PlayerControllerB> DespawnRpc =
        AccessTools.MethodDelegate<Action<PlayerControllerB>>(AccessTools.Method(typeof(PlayerControllerB), "DespawnHeldObjectServerRpc"));
    private readonly bool wasActive;
    private readonly Vector3 oldScale;
    private readonly Vector3 oldPosition;
    private readonly Quaternion oldRotation;
    private readonly string oldName;
    private readonly ulong oldActualId;
    private readonly int oldHealth;
    private readonly float oldStamina;
    private readonly bool oldExhausted;
    private readonly int oldItemSlot;
    private readonly int oldSuit;
    private readonly float oldWeight;
    private readonly bool wasInside;
    private readonly bool wasInElevator;
    private readonly bool wasInShip;
    private readonly bool wasDisconnected;
    private readonly Animator animator;
    private readonly bool animatorEnabled;
    private readonly RuntimeAnimatorController controller;
    private bool restoreWeapon;
    private GrabbableObject? pendingDrop;
    private float dropDeadline;
    private int restoreSlot = -1;
    private GrabbableObject? restoreItem;
    private bool walking;
    private bool terminalUsing;
    private bool leverUsing;
    private Vector3 lookPoint;
    private float lookPitch;
    private float lookPitchVelocity;
    private int rightHandLayer;
    private int bothHandsLayer;
    private int specialAnimationsLayer;
    private int emoteLayer;
    private string customEmote = string.Empty;
    private RuntimeAnimatorController? previousEmoteController;
    private RuntimeAnimatorController? customEmoteController;
    private bool customApiPlaying;
    private int betterEmote;
    private float nextCustomEmoteRetry;
    private int emote;
    private InteractTrigger? terminalPoseTrigger;
    private GrabbableObject? terminalPocketedItem;
    private GrabbableObject? animatedItem;
    private string? grabAnimation;
    private float holdingWeight;
    private Vector3 motionPosition;
    private Quaternion motionLook;
    private float lastMotionSample = float.NegativeInfinity;
    private float lastPhysicalMove = float.NegativeInfinity;
    private float lastLookMove = float.NegativeInfinity;
    private int motionFrame = -1;

    internal PlayerControllerB Player { get; }
    internal bool WalkingNow => walking;
    internal bool MovingForNutcracker(float distance)
    {
        float now = Time.time;
        if (motionFrame != Time.frameCount)
        {
            motionFrame = Time.frameCount;
            Vector3 position = Player.transform.position;
            Quaternion look = Player.gameplayCamera.transform.rotation;
            if (now - lastMotionSample > 0.4f)
            {
                motionPosition = position;
                motionLook = look;
                lastPhysicalMove = float.NegativeInfinity;
                lastLookMove = float.NegativeInfinity;
            }
            else
            {
                if ((position - motionPosition).sqrMagnitude >= 0.0001f)
                {
                    motionPosition = position;
                    lastPhysicalMove = now;
                }
                if (Quaternion.Angle(look, motionLook) >= 2f)
                {
                    motionLook = look;
                    lastLookMove = now;
                }
            }
            lastMotionSample = now;
        }
        return NutcrackerPolicy.Moving(Player.performingEmote, now - lastPhysicalMove, now - lastLookMove, distance);
    }

    internal bool TerminalUsing => terminalUsing;
    internal bool LeverUsing => leverUsing;
    internal bool DropPending => pendingDrop != null;
    internal bool Dead { get; private set; }
    internal CauseOfDeath DeathCause { get; private set; }
    internal string DeathAttacker { get; private set; } = "unknown";
    internal int DeathAnimation { get; private set; }
    internal Vector3 DeathVelocity { get; private set; }
    internal bool SpawnBody { get; private set; }
    internal Vector3 LookPoint => lookPoint;

    internal static IEnumerable<PlayerControllerB> Players => active.Keys;
    internal static bool Contains(PlayerControllerB player) => active.ContainsKey(player);
    internal static bool TryGet(PlayerControllerB player, out BotBody body) => active.TryGetValue(player, out body!);

    internal BotBody(PlayerControllerB player)
    {
        Player = player;
        var root = player.transform;
        wasActive = player.gameObject.activeSelf;
        oldPosition = root.position;
        oldRotation = root.rotation;
        oldScale = root.localScale;
        oldName = player.playerUsername;
        oldActualId = player.actualClientId;
        oldHealth = player.health;
        oldStamina = player.sprintMeter;
        oldExhausted = player.isExhausted;
        oldItemSlot = player.currentItemSlot;
        oldSuit = player.currentSuitID;
        oldWeight = player.carryWeight;
        wasInside = player.isInsideFactory;
        wasInElevator = player.isInElevator;
        wasInShip = player.isInHangarShipRoom;
        wasDisconnected = player.disconnectedMidGame;
        animator = player.playerBodyAnimator;
        controller = animator.runtimeAnimatorController;
        animatorEnabled = animator.enabled;

        active.Add(player, this);
        player.gameObject.SetActive(true);
        root.localScale = Vector3.one;
        SetName(player, $"Crew Bot {player.playerClientId}");
        player.actualClientId = NetworkManager.ServerClientId;
        player.health = 100;
        player.sprintMeter = 1f;
        player.isExhausted = false;
        player.carryWeight = 1f;
        player.isPlayerDead = false;
        player.disconnectedMidGame = false;
        player.isPlayerControlled = true;
        player.DisablePlayerModel(player.gameObject, enable: true, disableLocalArms: true);
        animator.enabled = true;
        animator.runtimeAnimatorController = player.playersManager.localClientAnimatorController;
        RefreshAnimatorLayers();
        player.performingEmote = false;
        player.emoteLayerWeight = 0f;
        animator.SetInteger(EmoteNumber, 0);
        animator.SetFloat(AnimationSpeed, 1f);
    }

    internal void Pose(Vector3 point, Quaternion rotation, bool moving, bool inside, bool aboard, bool sprinting = false, Vector3 velocity = default)
    {
        if (customEmoteController != null && animator.runtimeAnimatorController != customEmoteController)
        {
            animator.runtimeAnimatorController = customEmoteController;
            RefreshAnimatorLayers();
            animator.SetInteger(EmoteNumber, betterEmote);
        }
        if (customEmote.Length > 0 && !customApiPlaying && Time.time >= nextCustomEmoteRetry && EmoteCompatibility.IsApi(customEmote))
        {
            nextCustomEmoteRetry = Time.time + 1f;
            if (EmoteCompatibility.TryPlayApi(Player, customEmote))
            {
                customApiPlaying = true;
                emote = -1;
                SetEmote(1);
            }
        }
        Player.emoteLayerWeight = Mathf.MoveTowards(Player.emoteLayerWeight, emote != 0 ? 1f : 0f, Time.deltaTime * 10f);
        if (emoteLayer >= 0)
            animator.SetLayerWeight(emoteLayer, Player.emoteLayerWeight);
        Player.transform.SetPositionAndRotation(point, rotation);
        if (Player.isInElevator != aboard || Player.isInHangarShipRoom != aboard)
            Player.SetAllItemsInElevator(aboard, aboard);
        Player.isInsideFactory = inside;
        Player.isInElevator = aboard;
        Player.isInHangarShipRoom = aboard;
        Player.isSprinting = sprinting;
        animator.SetBool(Sprinting, sprinting);
        bool specialAnimation = terminalUsing || leverUsing || Player.inSpecialInteractAnimation || Player.inAnimationWithEnemy != null;
        Player.specialAnimationWeight = Mathf.MoveTowards(Player.specialAnimationWeight, specialAnimation ? 1f : 0f, Time.deltaTime * 12f);
        if (specialAnimationsLayer >= 0)
            animator.SetLayerWeight(specialAnimationsLayer, Player.specialAnimationWeight);
        UpdateHoldingAnimation();
        Vector3 localVelocity = Quaternion.Inverse(rotation) * velocity;
        if (moving && localVelocity.sqrMagnitude > 0.01f)
        {
            Vector3 direction = localVelocity.normalized;
            Player.moveInputVector = new Vector2(direction.x, direction.z);
            bool sideways = MovementPolicy.MovesSideways(direction.x, direction.z);
            animator.SetBool(Sideways, sideways);
            animator.SetFloat(AnimationSpeed, MovementPolicy.AnimationDirection(direction.x, direction.z, sideways));
        }
        else
        {
            Player.moveInputVector = Vector2.zero;
            animator.SetBool(Sideways, false);
        }
        if (walking == moving)
            return;

        walking = moving;
        animator.SetBool(Walking, moving);
    }

    private void UpdateHoldingAnimation()
    {
        var item = Player.currentlyHeldObjectServer;
        if (item == null || !item.isHeld || item.itemProperties == null || !Player.isHoldingObject)
            item = null;
        if (terminalUsing && item is Shovel)
            item = null;

        if (animatedItem != item)
        {
            if (!string.IsNullOrEmpty(grabAnimation))
                animator.SetBool(grabAnimation, false);

            animatedItem = item;
            grabAnimation = item != null ? item.itemProperties.grabAnim : null;
            animator.SetBool(GrabParameter, item != null);
            animator.SetBool(GrabValidated, item != null);
            animator.SetBool(CancelHolding, item == null);
            if (!string.IsNullOrEmpty(grabAnimation))
                animator.SetBool(grabAnimation, true);
        }

        holdingWeight = Mathf.MoveTowards(holdingWeight, item != null ? 1f : 0f, 25f * Time.deltaTime);
        if (rightHandLayer >= 0)
            animator.SetLayerWeight(rightHandLayer, holdingWeight);
        if (bothHandsLayer >= 0)
            animator.SetLayerWeight(bothHandsLayer, item != null && item.itemProperties.twoHandedAnimation ? holdingWeight : 0f);
    }

    internal void ApplyDamage(int damage, CauseOfDeath cause, int animation, Vector3 velocity, bool spawnBody)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || Dead || damage <= 0)
            return;

        Player.health = CombatRules.HealthAfterDamage(Player.health, damage);
        if (Player.health > 0)
        {
            Player.DamagePlayerClientRpc(damage, Player.health);
            return;
        }
        DeathCause = cause;
        DeathAnimation = animation;
        DeathVelocity = velocity;
        SpawnBody = spawnBody;
        DropAll();
        Dead = true;
        var enemy = EnemyBotContext.Current == Player ? EnemyBotContext.CurrentEnemy : null;
        string attacker = enemy != null ? enemy.enemyType != null && !string.IsNullOrEmpty(enemy.enemyType.enemyName)
            ? enemy.enemyType.enemyName : enemy.GetType().Name : "unknown";
        DeathAttacker = attacker;
        Plugin.Log.LogInfo($"Bot {Player.playerClientId} died: cause={cause}, attacker={attacker}, position={Player.transform.position}, body={spawnBody}.");
    }

    internal void SetCrouching(bool crouch)
    {
        if (Player.isCrouching == crouch)
            return;
        Player.isCrouching = crouch;
        if (crouch)
            animator.SetTrigger("startCrouching");
        animator.SetBool(Crouching, crouch);
    }

    internal void SetJumping(bool value)
    {
        animator.SetBool("Jumping", value);
    }

    internal void SetEmote(int value)
    {
        if (customEmote.Length > 0)
            value = customApiPlaying ? 0 : betterEmote > 0 ? betterEmote : 1;
        if (emote == value)
            return;
        emote = value;
        Player.performingEmote = value != 0;
        animator.SetInteger(EmoteNumber, value);
    }

    private void RefreshAnimatorLayers()
    {
        rightHandLayer = animator.GetLayerIndex("HoldingItemsRightHand");
        bothHandsLayer = animator.GetLayerIndex("HoldingItemsBothHands");
        specialAnimationsLayer = animator.GetLayerIndex("SpecialAnimations");
        emoteLayer = animator.GetLayerIndex("EmotesNoArms");
    }

    internal void SetCustomEmote(string key)
    {
        if (customEmote == key)
            return;
        if (customApiPlaying)
            EmoteCompatibility.StopApi(Player);
        if (previousEmoteController != null)
        {
            animator.runtimeAnimatorController = previousEmoteController;
            previousEmoteController = null;
            RefreshAnimatorLayers();
        }
        customApiPlaying = false;
        betterEmote = 0;
        customEmoteController = null;
        customEmote = key;
        emote = -1;
        if (key.Length == 0)
        {
            SetEmote(0);
            return;
        }
        var better = EmoteCompatibility.BetterController(key);
        int number = EmoteCompatibility.BetterNumber(key);
        if (better != null && number != 0)
        {
            previousEmoteController = animator.runtimeAnimatorController;
            customEmoteController = better;
            animator.runtimeAnimatorController = better;
            RefreshAnimatorLayers();
            betterEmote = number;
        }
        else
            customApiPlaying = EmoteCompatibility.TryPlayApi(Player, key);
        nextCustomEmoteRetry = Time.time + 1f;
        SetEmote(1);
    }

    internal void SetTerminalPose(bool usingTerminal)
    {
        if (terminalUsing == usingTerminal)
            return;
        var terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
        var trigger = terminal != null ? TerminalTrigger(terminal) ?? terminal.GetComponent<InteractTrigger>() : terminalPoseTrigger;
        if (trigger == null)
        {
            if (!usingTerminal)
            {
                terminalUsing = false;
                Player.inTerminalMenu = false;
                if (Player.currentTriggerInAnimationWith == terminalPoseTrigger)
                {
                    Player.currentTriggerInAnimationWith = null;
                    Player.inSpecialInteractAnimation = false;
                }
                terminalPoseTrigger = null;
                RestoreTerminalItem();
            }
            return;
        }
        terminalUsing = usingTerminal;
        if (usingTerminal)
        {
            if (Player.currentlyHeldObjectServer is Shovel heldWeapon)
            {
                terminalPocketedItem = heldWeapon;
                BotItemActions.Pocket(heldWeapon);
            }
            terminalPoseTrigger = trigger;
            Player.inTerminalMenu = true;
            Player.inSpecialInteractAnimation = true;
            Player.currentTriggerInAnimationWith = trigger;
            SetCrouching(false);
        }
        else
        {
            Player.inTerminalMenu = false;
            if (Player.currentTriggerInAnimationWith == terminalPoseTrigger)
            {
                Player.currentTriggerInAnimationWith = null;
                Player.inSpecialInteractAnimation = false;
            }
            terminalPoseTrigger = null;
            RestoreTerminalItem();
        }
        if (terminal != null)
        {
            if (terminal.terminalLight != null && (usingTerminal || !terminal.terminalInUse))
                terminal.terminalLight.enabled = usingTerminal;
            if (terminal.terminalAudio != null)
            {
                var clip = usingTerminal ? terminal.enterTerminalSFX : terminal.leaveTerminalSFX;
                if (clip != null)
                    terminal.terminalAudio.PlayOneShot(clip);
            }
        }
        string animation = usingTerminal ? trigger.animationString : trigger.stopAnimationString;
        if (!string.IsNullOrEmpty(animation))
        {
            animator.ResetTrigger(animation);
            animator.SetTrigger(animation);
        }
    }

    private void RestoreTerminalItem()
    {
        if (terminalPocketedItem != null && terminalPocketedItem == Player.currentlyHeldObjectServer && terminalPocketedItem.isHeld)
            BotItemActions.Equip(terminalPocketedItem);
        terminalPocketedItem = null;
    }

    internal void SetLeverPose(bool usingLever)
    {
        if (leverUsing == usingLever)
            return;
        leverUsing = usingLever;
        var lever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
        var trigger = lever != null ? lever.triggerScript : null;
        if (trigger == null)
            return;
        string animation = usingLever ? trigger.animationString : trigger.stopAnimationString;
        if (!string.IsNullOrEmpty(animation))
            animator.SetTrigger(animation);
    }

    internal static void SetName(PlayerControllerB player, string name)
    {
        player.playerUsername = name;
        if (player.usernameBillboardText != null)
            player.usernameBillboardText.text = name;
    }

    internal void SetLook(Vector3 point, float aimSpeed = 320f, bool preventDownward = false)
    {
        if ((point - Player.gameplayCamera.transform.position).sqrMagnitude < 0.01f)
        {
            if (!preventDownward)
                return;
            point = Player.gameplayCamera.transform.position + Player.transform.forward;
        }
        lookPoint = point;
        Vector3 localDirection = Player.transform.InverseTransformDirection(point - Player.gameplayCamera.transform.position);
        float pitch = Mathf.Clamp(-Mathf.Atan2(localDirection.y, new Vector2(localDirection.x, localDirection.z).magnitude) * Mathf.Rad2Deg, -80f, 80f);
        lookPitch = Mathf.SmoothDampAngle(lookPitch, pitch, ref lookPitchVelocity, 0.12f, aimSpeed, Time.deltaTime);
        if (preventDownward && lookPitch > 0f)
        {
            lookPitch = 0f;
            lookPitchVelocity = 0f;
        }
        Player.gameplayCamera.transform.rotation = Quaternion.Euler(lookPitch, Player.transform.eulerAngles.y, 0f);
        Player.playerEye.position = Player.gameplayCamera.transform.position;
        Player.playerEye.rotation = Player.gameplayCamera.transform.rotation;
    }

    internal int FreeScrapSlots()
    {
        int count = 0;
        foreach (var item in Player.ItemSlots)
            if (item == null)
                count++;
        return count;
    }

    internal GrabbableObject? RechargeableItem()
    {
        GrabbableObject? result = null;
        float lowest = 0.9f;
        foreach (var item in Player.ItemSlots)
            SelectRechargeable(item, ref result, ref lowest);
        SelectRechargeable(Player.ItemOnlySlot, ref result, ref lowest);
        return result;
    }

    private static void SelectRechargeable(GrabbableObject? item, ref GrabbableObject? result, ref float lowest)
    {
        if (item == null || !item.IsSpawned || !item.isHeld || item.itemProperties == null ||
            item.itemProperties.isScrap || !item.itemProperties.requiresBattery || item.insertedBattery == null ||
            item.insertedBattery.charge >= lowest)
            return;
        lowest = item.insertedBattery.charge;
        result = item;
    }

    internal bool CanCarry(GrabbableObject item)
    {
        if (item == null || item.itemProperties == null || LightningHazard.IsTarget(item))
            return false;

        var current = Player.currentlyHeldObjectServer;
        if (current != null && current.itemProperties != null && current.itemProperties.twoHanded)
            return false;
        return InventoryPolicy.HasRoom(FreeScrapSlots(), Player.ItemOnlySlot == null, item.itemProperties.isScrap,
            item.itemProperties.twoHanded, item.itemProperties.disallowUtilitySlot);
    }

    internal bool CanCarryAfterPocket(GrabbableObject item)
    {
        if (CanCarry(item))
            return true;
        if (item == null || item.itemProperties == null || LightningHazard.IsTarget(item))
            return false;
        var held = Player.currentlyHeldObjectServer;
        if (held != null && held.itemProperties != null && held.itemProperties.twoHanded &&
            (held.itemProperties.isScrap || held is RagdollGrabbableObject))
            return false;
        return InventoryPolicy.HasRoom(FreeScrapSlots(), Player.ItemOnlySlot == null, item.itemProperties.isScrap,
            item.itemProperties.twoHanded, item.itemProperties.disallowUtilitySlot);
    }

    internal bool PreparePickup(GrabbableObject? item = null)
    {
        var current = Player.currentlyHeldObjectServer;
        if (current == null || !current.itemProperties.twoHanded &&
            (item == null || !item.itemProperties.twoHanded || current is Shovel))
            return true;
        for (int index = 0; index < Player.ItemSlots.Length; index++)
        {
            if (Player.ItemSlots[index] != null)
                continue;
            SwitchSlot(Player, index, null!);
            SwitchSlotRpc(Player, index);
            return true;
        }
        if (Player.ItemOnlySlot == null)
        {
            SwitchSlot(Player, 50, null!);
            SwitchSlotRpc(Player, 50);
            return true;
        }
        return false;
    }

    internal void PocketNoisyItem()
    {
        if (!(Player.currentlyHeldObjectServer is AnimatedItem item) || item.itemProperties == null ||
            item.itemProperties.twoHanded || item.isPocketed || item.itemAudio == null || !item.itemAudio.isPlaying || item.noiseLoudness < 0.25f)
            return;
        int slot = -1;
        for (int index = 0; index < Player.ItemSlots.Length; index++)
        {
            var candidate = Player.ItemSlots[index];
            if (candidate == null)
            {
                slot = index;
                break;
            }
            if (slot < 0 && candidate != item && !(candidate is AnimatedItem))
                slot = index;
        }
        if (slot < 0 && Player.ItemOnlySlot != item && !(Player.ItemOnlySlot is AnimatedItem))
            slot = 50;
        if (slot < 0)
            return;
        SwitchSlot(Player, slot, null!);
        SwitchSlotRpc(Player, slot);
    }

    internal void PocketHeldWeapon()
    {
        if (!(Player.currentlyHeldObjectServer is Shovel weapon) || pendingDrop != null)
            return;
        int slot = -1;
        for (int index = 0; index < Player.ItemSlots.Length; index++)
        {
            var item = Player.ItemSlots[index];
            if (item == null)
            {
                slot = index;
                break;
            }
            if (slot < 0 && item != weapon && !(item is Shovel))
                slot = index;
        }
        if (slot < 0 && Player.ItemOnlySlot != weapon && !(Player.ItemOnlySlot is Shovel))
            slot = 50;
        if (slot < 0)
            return;
        SwitchSlot(Player, slot, null!);
        SwitchSlotRpc(Player, slot);
    }

    internal GrabbableObject? DangerousNoisyCargo()
    {
        foreach (var item in Player.ItemSlots)
        {
            if (item is AnimatedItem animated && item.IsSpawned && item.isHeld && item.itemProperties != null &&
                item.itemProperties.isScrap && animated.itemAudio != null && animated.itemAudio.isPlaying &&
                animated.noiseRange > 0f && animated.noiseLoudness * (item.isPocketed ? 0.5f : 1f) >= 0.25f)
                return item;
        }
        return null;
    }

    internal bool DropItem(GrabbableObject item, bool aboard = false)
    {
        if (pendingDrop != null || item == null || !item.IsSpawned ||
            Player.currentlyHeldObjectServer != item && Player.twoHanded || !EquipItem(item))
            return false;
        return Drop(aboard);
    }

    internal GunAmmo? FindAmmo(int ammoType)
    {
        if (Player.ItemOnlySlot is GunAmmo utility && utility.ammoType == ammoType)
            return utility;
        foreach (var item in Player.ItemSlots)
            if (item is GunAmmo ammo && ammo.ammoType == ammoType)
                return ammo;
        return null;
    }

    internal bool EquipItem(GrabbableObject item)
    {
        if (pendingDrop != null)
            return false;
        if (Player.currentlyHeldObjectServer == item)
            return true;
        int slot = Player.ItemOnlySlot == item ? 50 : Array.IndexOf(Player.ItemSlots, item);
        if (slot < 0 || LightningHazard.IsTarget(item))
            return false;
        SwitchSlot(Player, slot, null!);
        SwitchSlotRpc(Player, slot);
        return true;
    }

    internal T? FindItem<T>() where T : GrabbableObject
    {
        if (Player.ItemOnlySlot is T utility)
            return utility;
        foreach (var item in Player.ItemSlots)
            if (item is T found)
                return found;
        return null;
    }

    internal bool EquipWeapon()
    {
        if (Player.currentlyHeldObjectServer is Shovel)
            return true;
        if (Player.currentlyHeldObjectServer != null && Player.currentlyHeldObjectServer.itemProperties.twoHanded &&
            !(Player.currentlyHeldObjectServer is ShotgunItem shotgun && !shotgun.isReloading))
            return false;
        if (Player.ItemOnlySlot is Shovel utility && EquipItem(utility))
            return true;
        foreach (var item in Player.ItemSlots)
            if (item is Shovel && EquipItem(item))
                return true;
        return false;
    }

    internal bool Grab(GrabbableObject item)
    {
        if (pendingDrop != null || Blackboard.GuardedBeeHive(item) || Blackboard.HoarderNearItem(item) || !CanCarry(item) || item.isHeld || item.heldByPlayerOnServer || item.isHeldByEnemy)
            return false;

        GrabRpc(Player, item.NetworkObject);
        if (Player.currentlyHeldObjectServer != item)
            return false;

        item.parentObject = Player.serverItemHolder;
        item.playerHeldBy = Player;
        Blackboard.ForgetHumanDrop(item);
        Player.isHoldingObject = true;
        Player.carryWeight = Mathf.Clamp(Player.carryWeight + item.itemProperties.weight - 1f, 1f, 10f);
        return true;
    }

    internal bool PlaceLockPicker(LockPicker picker, DoorLock door)
    {
        if (pendingDrop != null || picker == null || door == null || !door.isLocked || door.isPickingLock ||
            door.NetworkObject == null || door.lockPickerPosition == null || door.lockPickerPosition2 == null ||
            !EquipItem(picker))
            return false;
        bool first = (door.lockPickerPosition.position - Player.transform.position).sqrMagnitude <
            (door.lockPickerPosition2.position - Player.transform.position).sqrMagnitude;
        Vector3 offset = first ? door.lockPickerPosition.localPosition : door.lockPickerPosition2.localPosition;
        Player.PlaceGrabbableObject(door.transform, offset, true, picker);
        BotItemActions.Discard(picker);
        PlaceRpc(Player, picker.NetworkObject, door.NetworkObject, offset, true);
        picker.PlaceLockPickerServerRpc(door.NetworkObject, first);
        picker.PlaceOnDoor(door, first);
        restoreWeapon = true;
        return true;
    }

    internal bool Drop(bool aboard, Vector3? target = null)
    {
        var item = Player.currentlyHeldObjectServer;
        if (pendingDrop != null || item == null || !item.IsSpawned)
            return false;

        var parent = aboard ? Player.playersManager.elevatorTransform : Player.playersManager.propsContainer;
        Vector3 dropPosition = Player.transform.position;
        Vector3 ahead = dropPosition + Player.transform.forward * 0.75f;
        if (!Physics.Linecast(dropPosition + Vector3.up * 0.5f, ahead + Vector3.up * 0.5f,
            Player.playersManager.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            dropPosition = ahead;
        Vector3 floor = item.GetItemFloorPosition((target ?? dropPosition) + Vector3.up);
        Vector3 localFloor = parent.InverseTransformPoint(floor);
        Player.SetObjectAsNoLongerHeld(aboard, aboard, localFloor, item);
        item.isInFactory = Player.isInsideFactory;
        BotItemActions.Discard(item);
        pendingDrop = item;
        dropDeadline = Time.time + 1.5f;
        restoreWeapon = true;
        DropRpc(Player, item.NetworkObject, aboard, aboard, localFloor, -1);
        return true;
    }

    internal bool DropLightningTarget()
    {
        GrabbableObject? item = Player.currentlyHeldObjectServer;
        if (!LightningHazard.IsTarget(item))
        {
            item = Player.ItemOnlySlot;
            if (!LightningHazard.IsTarget(item))
            {
                item = null;
                for (int i = 0; i < Player.ItemSlots.Length; i++)
                {
                    if (!LightningHazard.IsTarget(Player.ItemSlots[i]))
                        continue;
                    item = Player.ItemSlots[i];
                    break;
                }
            }
        }

        if (item == null || !item.IsSpawned)
            return false;

        var previous = Player.currentlyHeldObjectServer;
        if (previous != null && previous != item)
        {
            ReleaseItem(item);
            Player.isHoldingObject = true;
            Player.twoHanded = previous.itemProperties != null && previous.itemProperties.twoHanded;
            Player.twoHandedAnimation = previous.itemProperties != null && previous.itemProperties.twoHandedAnimation;
        }
        else
        {
            ReleaseItem(item);
            Player.currentlyHeldObjectServer = null;
            restoreWeapon = true;
        }
        if (pendingDrop == item)
            pendingDrop = null;
        if (restoreItem == item)
        {
            restoreItem = null;
            restoreSlot = -1;
        }
        Plugin.Log.LogInfo($"Bot {Player.playerClientId} dropped lightning target {item.name}.");
        return true;
    }

    internal void EquipStoredWeapon(bool allowWeapon)
    {
        if (pendingDrop != null)
        {
            if (Player.currentlyHeldObjectServer == pendingDrop)
            {
                if (Time.time < dropDeadline)
                    return;
                Player.currentlyHeldObjectServer = null;
            }
            pendingDrop = null;
        }
        if (!restoreWeapon || Player.currentlyHeldObjectServer != null)
            return;
        restoreWeapon = false;
        if (restoreItem != null && restoreSlot >= 0)
        {
            var item = restoreSlot == 50 ? Player.ItemOnlySlot :
                restoreSlot < Player.ItemSlots.Length ? Player.ItemSlots[restoreSlot] : null;
            int previousSlot = restoreSlot;
            restoreSlot = -1;
            restoreItem = null;
            if (item != null && (allowWeapon || !(item is Shovel)) && !LightningHazard.IsTarget(item))
            {
                SwitchSlot(Player, previousSlot, null!);
                SwitchSlotRpc(Player, previousSlot);
                return;
            }
        }
        int slot = -1;
        for (int i = 0; i < Player.ItemSlots.Length; i++)
        {
            var carried = Player.ItemSlots[i];
            if (carried != null && !(carried is Shovel) && !(carried is KeyItem) && !LightningHazard.IsTarget(carried))
            {
                slot = i;
                break;
            }
        }
        if (slot >= 0)
        {
            SwitchSlot(Player, slot, null!);
            SwitchSlotRpc(Player, slot);
            return;
        }
        if (!allowWeapon)
            return;
        if (Player.ItemOnlySlot is Shovel && !LightningHazard.IsTarget(Player.ItemOnlySlot))
            slot = 50;
        else
            for (int i = 0; i < Player.ItemSlots.Length; i++)
                if (Player.ItemSlots[i] is Shovel && !LightningHazard.IsTarget(Player.ItemSlots[i]))
                {
                    slot = i;
                    break;
                }
        if (slot < 0)
            return;
        Player.currentlyHeldObjectServer = null;
        SwitchSlot(Player, slot, null!);
        SwitchSlotRpc(Player, slot);
    }

    internal void DropAll()
    {
        bool hasItems = Player.ItemOnlySlot != null;
        if (!hasItems)
            foreach (var item in Player.ItemSlots)
                if (item != null)
                {
                    hasItems = true;
                    break;
                }
        if (!hasItems)
            return;
        foreach (var item in Player.ItemSlots)
            ReleaseItem(item);
        ReleaseItem(Player.ItemOnlySlot);
        Array.Clear(Player.ItemSlots, 0, Player.ItemSlots.Length);
        Player.ItemOnlySlot = null;
        Player.currentlyHeldObjectServer = null;
        Player.isHoldingObject = false;
        Player.twoHanded = false;
        Player.twoHandedAnimation = false;
        Player.carryWeight = 1f;
        restoreWeapon = false;
        pendingDrop = null;
        restoreItem = null;
    }

    private void ReleaseItem(GrabbableObject? item)
    {
        if (item == null || !item.IsSpawned)
            return;
        bool aboard = Player.isInHangarShipRoom;
        var parent = aboard ? Player.playersManager.elevatorTransform : Player.playersManager.propsContainer;
        Vector3 floor = item.GetItemFloorPosition(Player.transform.position + Vector3.up * 0.5f);
        Vector3 localFloor = parent.InverseTransformPoint(floor);
        Player.SetObjectAsNoLongerHeld(aboard, aboard, localFloor, item);
        item.isInFactory = Player.isInsideFactory;
        BotItemActions.Discard(item);
        Plugin.SyncBotDroppedItem(Player, item, localFloor, aboard, Player.isInsideFactory);
    }

    internal bool UseKey(DoorLock door)
    {
        if (!(Player.currentlyHeldObjectServer is KeyItem key) || !key.IsSpawned || !door.isLocked || door.isPickingLock)
            return false;

        door.UnlockDoorSyncWithServer();
        DespawnLocal(Player);
        DespawnRpc(Player);
        return true;
    }

    public void Dispose()
    {
        SetCustomEmote(string.Empty);
        SetEmote(0);
        Player.emoteLayerWeight = 0f;
        if (emoteLayer >= 0)
            animator.SetLayerWeight(emoteLayer, 0f);
        SetJumping(false);
        if (terminalUsing)
            SetTerminalPose(false);
        active.Remove(Player);
        if (Player == null || !Player.IsOwnedByServer ||
            Player.playersManager != null && Player.playersManager.ClientPlayerList.ContainsValue((int)Player.playerClientId))
            return;

        if (Dead || Player.isPlayerDead)
        {
            Player.isPlayerControlled = false;
            Player.isPlayerDead = true;
            Player.DisablePlayerModel(Player.gameObject);
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress)
            SuitSelector.Restore(Player, oldSuit);
        Player.isPlayerControlled = false;
        Player.isPlayerDead = false;
        Player.disconnectedMidGame = wasDisconnected;
        Player.isInsideFactory = wasInside;
        Player.isInElevator = wasInElevator;
        Player.isInHangarShipRoom = wasInShip;
        SetName(Player, oldName);
        Player.actualClientId = oldActualId;
        Player.health = oldHealth;
        Player.sprintMeter = oldStamina;
        Player.isExhausted = oldExhausted;
        Player.currentItemSlot = oldItemSlot;
        Player.carryWeight = oldWeight;
        animator.SetBool(Walking, false);
        animator.SetBool(Sprinting, false);
        animator.SetBool(Crouching, false);
        animator.SetBool(GrabParameter, false);
        animator.SetBool(GrabValidated, false);
        animator.SetBool(CancelHolding, true);
        if (!string.IsNullOrEmpty(grabAnimation))
            animator.SetBool(grabAnimation, false);
        Player.isCrouching = false;
        Player.isSprinting = false;
        animator.runtimeAnimatorController = controller;
        animator.enabled = animatorEnabled;
        Player.DisablePlayerModel(Player.gameObject);
        Player.transform.SetPositionAndRotation(oldPosition, oldRotation);
        Player.transform.localScale = oldScale;
        if (!wasActive)
            Player.gameObject.SetActive(false);
        ModelReplacementCompatibility.Remove(Player);
    }
}
