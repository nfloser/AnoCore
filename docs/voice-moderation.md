# Native voice moderation

AnoCore enforces persisted `Voice` restrictions through the existing synchronous moderation snapshot policy. The voice hot path does not query MariaDB.

## Why listener overrides are used

CounterStrikeSharp exposes both player voice flags and receiver-specific listen overrides. AnoCore does not use `VoiceFlags.Muted` for an administrative mute because that native flag also prevents the muted player from hearing other players.

Instead, AnoCore applies `ListenOverride.Mute` on each current listener for the restricted sender. This blocks the sender from being heard while still allowing that sender to hear other players according to normal server voice rules.

The engine-independent module maps exactly three states: `Default`, `Mute` and `Hear`. The CounterStrikeSharp transport maps those values one-to-one to `ListenOverride`.

## Runtime flow

`ModerationCommunicationRuntime` exposes both `ModerationChatGate` and `ModerationVoiceGate`. Both use the same `IModerationCommunicationPolicy` and the same warmed moderation snapshots.

`ModerationVoiceCoordinator` periodically reconciles online listener/sender pairs. A sender is blocked when the shared voice gate returns blocked or snapshot-unavailable. Snapshot misses are therefore fail-closed.

The live plugin reconciles every 250 ms. Applying or revoking a mute updates the shared loaded moderation snapshot, so voice behavior can change without reconnecting.

## Override ownership

Before setting `Mute`, the coordinator records the existing override for the exact listener-session × sender-session pair. When the moderation mute ends, AnoCore restores that original value.

Session IDs are part of the ownership key. A stale disconnect/reconnect session therefore cannot restore an old override onto a new player session or reused slot.

If another system changes an override after AnoCore set its mute, AnoCore does not blindly restore its remembered original during unmute or unload. Restoration occurs only while the current value is still the `Mute` value AnoCore expects to own.

On plugin unload, still-current overrides owned by AnoCore are restored and the voice reconciliation timer is stopped before the moderation runtime is disposed.

## Native transport

`CounterStrikeVoiceModerationTransport` accepts only current connected player sessions from the shared registry, resolves current human controllers by SteamID64, rejects bots/HLTV/invalid controllers/stale sessions, and maps `Default`, `Mute` and `Hear` to CounterStrikeSharp listen overrides.

Entity lifetime races are treated as failed reads/writes instead of crashing the plugin. No second player registry, moderation cache or voice-state database is introduced.

## Real-server acceptance

CI verifies the engine-independent ownership/reconciliation behavior and compiles the API-374 native adapter. It cannot prove live CS2 voice routing.

Before production acceptance on a disposable CS2/DatHost server:

1. Connect at least two unrestricted human clients and verify normal voice.
2. Apply `!anomute`; other clients must stop hearing the target within the reconciliation interval.
3. Verify the muted target can still hear other players.
4. Apply `!anounmute`; previous/default voice routing must return without reconnect.
5. Apply `!anosilence`; both voice and chat restrictions must take effect.
6. Verify temporary mute expiry restores voice without reconnect.
7. Join a new listener while a target is muted; the new listener must not hear that sender.
8. Reconnect sender/listener, including slot reuse; no stale override may leak into the new sessions.
9. Verify pre-existing `Default` and `Hear` overrides are restored after AnoCore releases its mute.
10. Change an override from another system while AnoCore owns a mute; AnoCore must not overwrite that later external state when releasing.
11. Hot-reload/unload AnoCore and verify no player remains stuck muted.
12. Test team/all-talk voice settings so restoring `Default` returns control to normal game policy.
13. Verify multiple simultaneous muted senders remain independent.

Record CS2 build, CounterStrikeSharp version, AnoCore commit and observations in issue #61 / PR #62 or the production-verification workstream.

## Scope boundary

This package implements persisted voice-mute enforcement only. Kick/warnings, chat tags/colors, custom chat rewriting and administration HUD remain separate #17 work packages.
