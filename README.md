# Lethal Fake Client

A BepInEx 5 mod for Lethal Company. The host runs one adaptive Director and uses available player bodies for the crew. The mod has no dependency on other mods.

This project is experimental. A successful build or automated check does not establish that gameplay, multiplayer synchronization, or optional mod integrations work in every game version or mod setup.

## Requirements and build

- A legally installed copy of Lethal Company, with its game-managed assemblies.
- BepInEx 5, including `0Harmony.dll`, installed in the game or a mod-manager profile.
- .NET SDK 8 or later to build the mod and run the policy checks.

Game and BepInEx assemblies are local build dependencies; they are not included in this repository. Set the two paths to your own installation when building:

```sh
dotnet build -c Release \
  -p:GameManagedPath="/path/to/Lethal Company/Lethal Company_Data/Managed" \
  -p:BepInExPath="/path/to/profile/BepInEx/core"
```

On Windows, use the equivalent local paths in the same two properties. The project also recognizes these names as environment variables. On Linux, the default game path is the standard Steam library under the user's home directory; the default BepInEx path is the game installation or a local `clean` r2modman profile. Pass the properties explicitly when those defaults do not match.

The dependency-free policy checks can be run without the game assemblies:

```sh
dotnet run --project tests/PolicyChecks/PolicyChecks.csproj -c Release
```

GitHub Actions runs these checks and the emote compatibility checks without proprietary game assemblies. It does not build the plugin or simulate Unity navigation or multiplayer gameplay; build the plugin locally against your game installation.

Optional emote discovery can also be checked with `dotnet run --project tests/EmoteCompatibilityChecks/EmoteCompatibilityChecks.csproj -c Release`.

Idle ship bots can use installed BetterEmotes and LethalEmotesAPI animations. These integrations are optional; peers without a selected animation see the vanilla fallback. Compatible peers need the same bot-mod protocol version, and matching emote mods and packs to see the same custom animation.

## Run it

1. Build the mod and place `bin/Release/netstandard2.1/LethalFakeClient.dll` in the selected profile's `BepInEx/plugins/LethalFakeClient/` directory.
2. Restart the game. In multiplayer, every peer needs the same mod version; the host runs the Director.
3. Host a round and land on a moon. Bots do not spawn at the Company building. The Director fills unused slots with up to 128 bots, using a walkable position inside the ship; if ship navigation is not ready, it retries instead of spawning bots outside. If a peer joins, the host releases bot slots before the game assigns the new player's slot. The host resumes the team when all connected peers have identified a compatible version.

The mod does not add player slots by itself. With the unmodified four-player lobby, a solo host can have at most three bots. If no eligible slots are found, the host records slot status in `BepInEx/LogOutput.log` and retries when an unoccupied slot exists. A missed level-generation callback is recovered after the ship has landed.

ModelReplacementAPI is optional. When it is installed, bots can use its registered suit models without changing their real player identity. Multiplayer peers need the same bot and model mods to see those models.

## License

The project is licensed under the [GNU General Public License v3.0 only](LICENSE) (GPL-3.0-only). Lethal Company, BepInEx, and optional third-party mods are distributed separately under their own licenses.

## How the crew works

The Director uses one round blackboard and plans on task results, crew changes, and periodic validation. It records crew exploration during the round and prioritizes unvisited destinations. Bots choose among available suits, favoring those least used by the crew. Outdoor exploration is independent. After the crew observes an indoor threat, bots can form pairs inside the facility; a pair dissolves as soon as either bot leaves. Bots reserve scrap and bodies, escort partners, collect visible nearby scrap while inventory space and safety allow, stage it at facility exits, and deliver batches of at least four to the ship center, or shelter when the Director finds no useful safe task. Nearby threats interrupt tasks and trigger evasive movement. Indoors, bots try to reach a safe exit and avoid routes that pass close to the threat; if no exit route is safe, they first try to create distance. Entrances used to flee or found unsafe are temporarily shared with the crew, which can choose a different safe entrance. When at least two bots are available and ship support is enabled, the most injured bot is assigned to ship support; an existing guardian can maintain coverage while a replacement returns. A dangerous or blocked return can delay coverage.

The mod discovers vanilla and modded `EnemyAI` instances through the runtime registry and observes line of sight, movement, target, and indoor or outdoor context. For other enemy implementations, register a component and an `IThreatAdapter` via `ThreatSources.Register` on the Unity thread. Return a `ThreatSample` with the current position, zone, life status, and optional pursued player. Unregister the component when removing it.

Task outcomes, scrap value and delivery time, route failures, and observed encounters influence bounded, confidence-weighted estimates. Temporary locations, hazards, and reservations reset each round. The host saves learned estimates to `BepInEx/config/lethal-fake-client.learning.json` periodically and at round end. Deleting that file while the game is closed resets learning.

The host owns movement, task decisions, and learning. Clients receive versioned crew snapshots through Unity Netcode named messages and interpolate bot poses. Scrap pickup and drop use the game's inventory and network paths. The crew uses grounded NavMesh paths, facility entrances, ordinary and keyed doors, and the mineshaft elevator. Failed routes get a bounded retry before the target is temporarily avoided. Known mines, turrets, and quicksand are recorded and avoided; the game navigation mesh is used for walkable routes. Known hazards are represented by temporary NavMesh obstacles, which can also affect other NavMesh agents. While someone is inside the facility, a ship operator can cycle the radar, use terminal-accessible hazards or doors near crew members, and use an available ship teleporter for an observed immediate emergency. Teleporting drops the target’s equipment as in the game. Bots swing shovels and signs at close threats without deliberately stopping or pursuing them. The host handles bot damage and creates networked, recoverable bodies when the game's death type permits them; collision-based enemies use bot-aware targeting. Enemies with unusual custom attack code may require additional compatibility work. Lightning targets are dropped and avoided. Maps with disconnected navigation can still prevent travel.

In the mineshaft entrance room, exploring bots select an unvisited lower-floor destination and use the elevator when it is available. They try its navigable interior first and fall back to controlled boarding when the cabin has no navigation mesh. Elevator travel and modded mineshafts need in-game testing.

The Director remembers observed indoor scrap for the current round, considers estimated delivery distance when ranking it, and shares temporary route and death risks. Nearby bots can hand off keys or spare shovels when needed. An elevator operator waits briefly for nearby bots travelling to the same floor when conditions are safe.

## Configuration

BepInEx creates `BepInEx/config/dev.lethalfakeclient.cfg` after the first launch. The host controls bot decisions.

- `Crew.BotLimit`: maximum bots (0–128; available player slots still apply).
- `Crew.RandomSteamAvatars`: show Steam profile pictures for dead bots in the spectator UI (default disabled; host setting). IDs are selected without duplicates from the host's Steam friends who are not playing Lethal Company or connected to the lobby. If no unused profile is available, the bot uses the default picture. Other peers need the same plugin version.
- `Perception.ViewAngle`: horizontal view angle in degrees (60–360).
- `Behavior.RiskTolerance`: risk tolerance (0.5–1.5).
- `Behavior.CollectItems`, `RecoverBodies`, `DefendWithWeapons`, `ShipSupport`: enable the corresponding behaviors.
- `Behavior.ShipSupportStartHour`: earliest ship-operator time on the 24-hour game clock (6–24; default 14).
- `Behavior.CollectDropshipDeliveries`: assign an available nearby bot to open the landed dropship and carry ordered items to the ship (default enabled).
- `Extraction.ReturnHour`: ship return time on the 24-hour game clock, from 6 to 24; default 22.
- `Extraction.ReturnEarlierAfterDeaths`: move the return time earlier in proportion to crew deaths; enabled by default.

Build verification does not replace an in-game test. Full rounds, two-peer play, and compatibility with other mods still need in-game verification.

Bot assignments, outcomes, route failures, equipment use and deaths are recorded by the host in `BepInEx/config/lethal-fake-client.history.log` (rotated at 2 MiB). Death records identify an attacker when the game's collision context supplies one; other sources remain unknown.

Bots can take lockpickers and safe stun grenades from the ship, deploy a lockpicker on a locked door, and use a grenade against a distant visible threat when teammates are clear of the target area. After a slime is observed, an available bot can take a boombox from the ship, play it near the slime, stop it when unnecessary, and recharge it at the ship charger. These actions require the relevant items and an accessible route. Danger checks also reconsider active routes when a known hazard or an unprotected drop is immediately ahead.

Bots can take a shotgun and belt bag from the ship, or pick them up nearby when safe. A shotgun is fired at a visible threat without pursuing it, with a clear line of fire around teammates. Bots use compatible shells and the game's reload animation. Belt bags can hold non-scrap shotgun shells; bots take them out to reload. These behaviors require available inventory space and equipment, and still need in-game multiplayer validation.

Known firing or charging turrets trigger an evasive route, and the host applies turret hits to bots using the game's firing cadence. Butler avoidance depends on whether it targets that bot. Inactive Old Birds are ignored; active ones contribute a forward sight hazard that bots try to avoid. These interactions still require live gameplay and multiplayer testing.

A Nutcracker's actual tracked player is read from its current game state. The targeted bot tries to find reachable cover that blocks line of sight, and stays under cover while targeted. If no cover is reachable, it attempts to move away. Untargeted bots do not flee or attack it merely because it targets someone else; the usual response to an active inspection still applies. This behavior requires in-game validation.

Bots score scrap using value, distance, weight, inventory capacity and time remaining. At the ship, they recharge held non-scrap battery equipment and may store an unnecessary boombox, an unloaded shotgun with no available ammunition, or depleted equipment when inventory space is needed.

Scrap staged within eight metres of the same outside entrance becomes eligible for ship transport when four items have accumulated; an active batch continues until its remaining loose items are taken. The existing late-extraction and immediate danger behavior can take precedence. Steam valve effects are not treated as enemy threats.

A Hoarding Bug does not trigger threat responses until it enters its attack/chase state. Bots avoid collecting items within six metres of a living nearby Hoarding Bug, and check again immediately before pickup.

When an indoor Jester starts cranking, all indoor bots try to evacuate through a reachable safe exit. They remain outside while it is cranking or hunting and can enter again after it resets. Immediate hazards can still interrupt the evacuation.

The shared hazard map records observed enemies, mines and turrets for route decisions. Indoor bots periodically plan a reachable exit and recheck it when evacuation starts; if no planned route is available, they fall back to the existing escape search. Near departure, the Director estimates the remaining real time and travel distance, accounting for carried weight and cargo value before selecting another outdoor item. The first early departure vote recalls the crew to the ship, while the game continues to control the actual departure vote.

The configured extraction threshold moves earlier in proportion to deaths among crew who participated in the round when early return is enabled, excluding unused slots. Each quarter of the crew lost moves it one game hour earlier, up to four hours; revived crew no longer count as dead. Disconnections do not count as deaths. Once extraction begins, the usual ship-area collection rules apply. The game's automatic departure and travel-time safety checks still apply. Indoor group mode starts after an indoor enemy is observed and ends after 12 seconds without a new sighting. Outdoor enemies, unspawned enemies, mines, turrets and neutral area hazards do not directly trigger indoor grouping. Enemy sightings and group state reset between rounds, while learned outcome statistics persist.

On leaving a facility, bots search around the normal exit point for a clear landing connected to outside navigation nodes. If no verified landing can be found, they retry or choose another exit. Maps without outside nodes use the normal exit point when it is clear.

An indoor bot that makes no progress for three seconds tries nearby safe navigation nodes, including previously visited ones, then resumes its task or requests a new one. Followers and bots in special interactions are exempt. Bots look toward their pickup target, try an accessible nearby open interaction when loot is obstructed, and can collect it without line of sight within three horizontal metres if opening is unavailable.

Nearby living Eyeless Dogs cause bots to crouch and stop sprinting, independently of their current view target. Bots keep their distance, pocket suitable noisy items, postpone collecting noisy animated loot, and stop bot-generated crouching footstep events. If a dog appears to be pursuing the bot, it can sprint away and defend itself. Alert range has a buffer to avoid rapidly switching stance. Held items and other players can still produce sounds that attract dogs.

Mask Hornets, Manticoils, Roaming Locusts and Spore Lizards do not trigger enemy reactions. Vain Shroud, Cadaver growth and blooms, and Hygroderes are treated as areas to avoid; nearby vegetation and creatures contribute navigation obstacles and route risk. Bots already inside a hazardous area try to move toward a safer point. The existing boombox support remains available, but does not make slime safe to cross.

When an indoor exit fails, a bot temporarily avoids it and tries alternatives. After all known exits fail, or no exit remains available for eight seconds, it asks for ship teleport support; an available ship operator may teleport it when the equipment and radar permit. Teleportation drops carried items. A bot can put down actively noisy scrap near a dog and remembers it for retrieval once dogs have been away for eight seconds. Known mines and turrets are announced to the crew through game chat, and an available ship operator can use their terminal codes when a player is nearby and the device is ready.

An available bot aboard can close the ship door for a nearby observed outdoor enemy when no crew member is near the entrance and there is sufficient power. It reopens a door it closed after the threat has been absent for six seconds or power is low. Loot scoring accounts for the remaining quota, estimated ship cargo value, weight, travel, available inventory and deadline pressure. A bot under close pursuit can drop heavy or two-handed scrap, and the crew remembers it until the area is safe enough to retrieve it. Teleporter targets are ranked by immediate danger, health and whether they are trapped. Entrance outcomes and coarse outdoor route outcomes are learned per moon and saved across sessions; procedural indoor positions are not persisted.

The round continues after all human players die while a bot remains alive; ordinary midnight departure still applies. Broken vanilla wooden bridges are treated as blocked navigation areas. Bots can select reachable indoor loot without a viewing-angle requirement, collect safe nearby outdoor scrap while returning, and use the game's one-handed tool utility slot. Two-handed loot still occupies the bot's hands.
