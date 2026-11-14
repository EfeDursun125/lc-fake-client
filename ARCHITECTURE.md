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

The session owns the Director and bot instances until round cleanup. Each bot owns its navigation agent and body wrapper. Unity state and the caches below are accessed on the game thread.

| State | Key and lifetime | Invalidation / bound |
| --- | --- | --- |
| Contacts and round knowledge | Director blackboard, enemy or item | Cleared with the Director; stale contacts expire. |
| Bot replicas | Session, player slot | Removed when absent from authoritative snapshots, on death or round cleanup; limited by player slots. |
| Emote state | Bot body, replicated bot snapshot and reliable custom-emote events | Reset when the bot is no longer idle or is disposed; missing optional mods fall back to a game emote. |
| Optional emote metadata | `EmoteCompatibility`, reflected mod types and fields | Initialized after soft dependencies; selection lists are rebuilt on each emote choice. |
| Learned outcomes | Learning instance, outcome key | Persisted across rounds; maximum 2,048 entries. |

Only idle bots aboard the ship may perform random emotes. Ship operators and bots with active assignments do not emote. The host chooses the emote; snapshots carry the vanilla fallback while reliable events carry optional mod emote names. Custom animation playback is local on each peer, and absent mods use the fallback. Bot aim interpolates toward the existing targets without changing navigation decisions.

## Helper index

| Function | Purpose |
| --- | --- |
| `Director.IsShipOperator` | Identifies ship support and active ship operators. |
| `CrewBot.UpdateShipEmote` | Schedules and cancels idle ship emotes. |
| `BotBody.SetEmote` | Applies the selected player animation. |
| `ShipEmotePolicy.Pack`, `ShipEmotePolicy.Emote` | Encode and validate emotes in snapshots. |
| `ShipEmotePolicy.CanPerform` | Gate emotes on ship idleness and operator status. |
| `EmoteCompatibility.Choose`, `EmoteCompatibility.TryPlayApi` | Discover optional animations and play them on the bot's mapper. |
