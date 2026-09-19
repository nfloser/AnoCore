# Native chat moderation

AnoCore enforces persisted `Chat` moderation restrictions on the Counter-Strike 2 chat hot path without querying MariaDB for every message.

## Runtime flow

The shared `ModerationService` is also the `IModerationSnapshotProvider`.

`ModerationCommunicationRuntime` owns:

- one `ModerationSnapshotLifecycle`,
- one synchronous `IModerationCommunicationPolicy`,
- the chat-specific `ModerationChatGate`.

The lifecycle warms snapshots when a player connects or reconnects and invalidates the matching session on disconnect. During plugin activation, already connected players are explicitly bootstrapped because their connect events may have occurred before the communication lifecycle subscribed.

A bootstrap warm failure is isolated per player: remaining online players are still warmed. The failed player's snapshot stays unavailable and therefore remains fail-closed.

## Chat decision

`ModerationChatGate` evaluates only the cached snapshot:

- cached and no Chat restriction -> allow,
- cached and Chat restriction -> block,
- snapshot unavailable/not ready -> block.

The chat listener therefore performs no database access.

Applying or revoking moderation through the shared `ModerationService` updates an already-loaded snapshot atomically with the persisted state, so native chat decisions observe live gag/ungag changes without a reconnect.

## CounterStrikeSharp adapter

`CounterStrikeChatModerationAdapter` registers API-374 pre-command listeners for:

- `say`,
- `say_team`.

For a valid human player with a SteamID64, a blocked decision returns `HookResult.Handled`, preventing the native chat command from executing. An allowed decision returns `HookResult.Continue`.

Server-console callers, bots, HLTV and invalid player controllers are not mapped to a fake moderation identity. A valid human player whose SteamID is not yet available is blocked fail-closed.

The adapter explicitly unregisters both listeners on dispose. Plugin activation failure disposes the adapter and communication lifecycle; normal unload removes the listener before disposing snapshot lifecycle state.

## Real-server acceptance

CI proves the engine-independent decision/lifecycle behavior and compiles the CounterStrikeSharp API-374 adapter, but cannot establish real client chat ordering.

Before production acceptance on a disposable CS2/DatHost server:

1. connect an unrestricted player and verify public and team chat are visible;
2. apply `!anogag` and verify both `say` and `say_team` are suppressed immediately;
3. apply `!anoungag` and verify chat resumes immediately without reconnect;
4. apply `!anosilence` and verify chat is suppressed while later voice testing remains a separate package;
5. verify a temporary gag expires at its exact deadline without reconnect;
6. hot-reload AnoCore with players already connected and verify they are blocked only while their snapshots warm, then receive their persisted state;
7. simulate/reproduce a snapshot warm failure and verify that player remains fail-closed while other players are still warmed;
8. disconnect/reconnect and reuse client slots; stale sessions must not invalidate a newer snapshot;
9. unload/reload the plugin and verify no orphaned `say`/`say_team` listener remains;
10. verify CounterStrikeSharp chat command behavior while gagged (for example `!anostatus` or another registered command) and record whether command dispatch occurs before or after the native chat listener. Do not claim command-trigger compatibility until this is observed on the target CSS build.

Record CounterStrikeSharp version, CS2 server build, AnoCore commit and observations in issue #59 / PR #60 or the production-verification workstream.

## Scope boundary

This package does not implement voice mute enforcement, chat recoloring/tag formatting, custom message rewriting, admin HUDs or a replacement chat processor. Those remain separate #17 packages.
