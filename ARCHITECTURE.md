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
| `EmoteCompatibility` | Optional BetterEmotes and LethalEmotesAPI discovery and playback; no hard dependency. |

The session owns the Director and bot instances until round cleanup. Before the game revives players, dead bot slots are retired and released on each peer so the game does not revive them as human players; clients reject late bot state and death messages until the next landing. Each bot owns its navigation agent and body wrapper. Unity state and the caches below are accessed on the game thread.

| State | Key and lifetime | Invalidation / bound |
| --- | --- | --- |
| Contacts and round knowledge | Director blackboard, enemy or item | Cleared with the Director; stale contacts expire. |
| Bot replicas and names | Session, player slot | Authoritative host sends profile names reliably; replicas and names are removed on retirement or round cleanup, limited by player slots. |
| Selected Steam profiles | Bot player slot | Reserved until bot cleanup to avoid selecting a profile twice; real player identities are never changed. |
| Emote state | Bot body, replicated bot snapshot and reliable custom-emote events | Reset when the bot is no longer idle or is disposed; missing optional mods fall back to a game emote. |
| Optional emote metadata | `EmoteCompatibility`, reflected mod types and fields | Initialized after soft dependencies; selection lists are rebuilt on each emote choice. |
| Learned outcomes | Learning instance, outcome key | Persisted across rounds; maximum 2,048 entries. |

For ordinary travel, bots compare complete NavMesh routes through bounded alternate waypoints by length and shared risk, including observed enemies, environmental hazards, death locations, and spike crossings. They validate both legs against existing hazards and keep immediate escape behavior separate; the NavMesh polygons themselves are not modified.

Area hazards track living Circuit Bee swarms as moving risk areas and place temporary nearby navigation obstacles; bots already within the area can route toward safety. Bots discard carried flashlights aboard using the existing synchronized inventory drop path.

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
