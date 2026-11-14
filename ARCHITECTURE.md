# Architecture

The host runs the bot Director and mechanics; peers render replicated player bodies. Game and BepInEx assemblies are build-time references.

| Module | Responsibility |
| --- | --- |
| `Plugin`, `Session` | Game hooks, round lifetime, compatible-peer handshake, bot state and death replication. |
| `Director`, `Blackboard` | Shared perception, task and role allocation, hazards, round knowledge and coordination. |
| `CrewBot`, `BotBody` | Per-bot navigation, immediate reactions, inventory and player-model animation. |
| `Learning` | Bounded, persistent outcome estimates. |
| `*Policy` | Small decision rules shared with the standalone policy checks. |
| `BotAvatars`, `ModelReplacementCompatibility` | Optional presentation compatibility. |
| `BotHudScope`, `BotItemHudPatch` | Suppress local item control tips during bot-only inventory operations. |
| `TulipBotHooks`, `ThumperBotCollision` | Connect vanilla Tulip Snake and Thumper interactions to host-controlled bots. |
| `EmoteCompatibility` | Optional BetterEmotes and LethalEmotesAPI discovery and playback; no hard dependency. |

Bot inventory actions use a nested thread-local HUD scope so host-owned bot items do not overwrite or clear the local player's control tips; item behavior and synchronization still run normally.

The session owns the Director and bot instances until round cleanup. Before the game revives players, dead bot slots are retired and released on each peer so the game does not revive them as human players; clients reject late bot state, death and drop messages until the next landing. Remote bot operations check that a slot has not been assigned to a human. Each bot owns its navigation agent and body wrapper. Unity state and the caches below are accessed on the game thread.

Optional enemy collision methods are discovered once per loaded assembly; later assembly-load notifications are queued and patched on the game thread. Patch failures remain isolated to the affected optional method.

Protocol v12 binds messages to a random host-session token, a round generation, and a monotonically increasing bot-slot generation. A reliable challenge/response initializes each peer; snapshots establish slot generations before reliable events. All bot messages share the same reliable sequenced delivery. Retired generations accept late cleanup events but cannot be recreated by old snapshots. A closed round rejects its old generation after landing.

| State | Key and lifetime | Invalidation / bound |
| --- | --- | --- |
| Contacts and round knowledge | Director blackboard, enemy or item | Cleared with the Director; stale contacts expire. |
| Bot replicas and names | Session, round, player slot and bot generation | Authoritative host sends profile names reliably; replicas and names are removed on retirement or round cleanup, limited by player slots. |
| Selected Steam profiles | Bot player slot | Reserved until bot cleanup to avoid selecting a profile twice; real player identities are never changed. |
| Emote state | Bot body, replicated bot snapshot and reliable custom-emote events | Reset when the bot is no longer idle or is disposed; missing optional mods fall back to a game emote. |
| Optional emote metadata | `EmoteCompatibility`, reflected mod types and fields | Initialized after soft dependencies; selection lists are rebuilt on each emote choice. |
| Mineshaft elevator seats | Director, participating bot | Up to four distinct boarding positions are reserved until each bot exits or aborts; only the owner operates the controls. |
| Thumper collision cache | Thumper instance | Cached trigger and throttled proximity check; removed when the enemy is destroyed. |
| Nutcracker movement samples | Bot body, frame | Reset after observation gaps; actual position and camera rotation are sampled once per frame. |
| Learned outcomes | Learning instance, outcome key | Persisted across rounds; maximum 2,048 entries. |

Tulip Snake uses its game conductor for bot attachment, flap and release timing; while airborne, the host bot temporarily yields NavMesh movement to CharacterController flight and fall physics before returning to navigation. Thumpers near a bot target invoke the game's normal collision attack when their colliders are close and unobstructed.

Bots sharing a mineshaft trip board assigned positions and the controlling bot waits briefly for eligible nearby crew. Nutcracker bot detection uses observed displacement, close-range camera turns and emotes rather than a potentially stale walking animation flag.

Partial NavMesh paths may be bridged by bounded jump searches around multiple candidate edges and elevations; partial routes beyond the landing must make progress, and both sampled endpoints and the airborne arc are checked before a shared jump link is created.

For ordinary travel, bots compare complete NavMesh routes through bounded alternate waypoints by length and shared risk, including observed enemies, environmental hazards, death locations, and spike crossings. They validate both legs against existing hazards and keep immediate escape behavior separate. Existing door carving is temporarily adjusted, and broken bridges and quicksand retain shared carving.

Hive selection and pickup check cached Circuit Bee instances and their runtime hive references; a nearby active, non-departing swarm temporarily excludes that hive while unguarded hives remain eligible. Area hazards, mines and turrets use bot-specific risk checks and periodic short-range route validation without carving the shared NavMesh. Broken bridges and quicksand retain temporary carving obstacles, which are destroyed with the hazard tracker. Bots inside a hazard may route toward lower risk. Bots discard carried flashlights aboard using the existing synchronized inventory drop path.

Only idle bots aboard the ship may perform random emotes. Ship operators and bots with active assignments do not emote. The host chooses the emote; snapshots carry the vanilla fallback while reliable events carry optional mod emote names. Custom animation playback is local on each peer, and absent mods use the fallback. Bot aim interpolates toward the existing targets without changing navigation decisions. During entrance transitions, the look target and replicated pitch are restricted to the horizon or above.

## Helper index

| Function | Purpose |
| --- | --- |
| `Director.IsShipOperator` | Identifies ship support and active ship operators. |
| `CrewBot.UpdateShipEmote` | Schedules and cancels idle ship emotes. |
| `BotBody.SetEmote` | Applies the selected player animation. |
| `ShipEmotePolicy.Pack`, `ShipEmotePolicy.Emote` | Encode and validate emotes in snapshots. |
| `ShipEmotePolicy.CanPerform` | Gate emotes on ship idleness and operator status. |
| `EmoteCompatibility.Choose`, `EmoteCompatibility.TryPlayApi` | Discover optional animations and play them on the bot's mapper. |
