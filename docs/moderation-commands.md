# Moderation admin commands

The `AnoCore.Modules.Admin` project provides engine-independent administration commands on top of AnoCore's shared targeting, authorization and persistent moderation services.

These commands change durable moderation state. Native CounterStrikeSharp enforcement is a separate integration layer: until that layer is wired, a stored ban does not itself disconnect a connected client and stored voice/chat restrictions do not themselves suppress packets or messages.

## Commands

| Command | Permission | Behavior |
| --- | --- | --- |
| `!anoban <target> <minutes> [reason]` | `ano.admin.ban` | Apply a connect restriction |
| `!anounban <target> [reason]` | `ano.admin.unban` | Revoke active connect restrictions |
| `!anomute <target> <minutes> [reason]` | `ano.admin.mute` | Apply a voice restriction |
| `!anounmute <target> [reason]` | `ano.admin.unmute` | Revoke active voice restrictions |
| `!anogag <target> <minutes> [reason]` | `ano.admin.gag` | Apply a text-chat restriction |
| `!anoungag <target> [reason]` | `ano.admin.ungag` | Revoke active text-chat restrictions |
| `!anosilence <target> <minutes> [reason]` | `ano.admin.silence` | Apply voice + text-chat restrictions |
| `!anounsilence <target> [reason]` | `ano.admin.unsilence` | Revoke active voice + text-chat restrictions |

A duration of `0` means permanent. Negative durations and timestamp-overflowing durations are rejected. Multi-word reasons must be quoted, for example:

`!anoban SomePlayer 60 "repeated griefing"`

If the optional reason is omitted, AnoCore stores `No reason provided.`.

## Kick commands

| Command | Permission | Behavior |
| --- | --- | --- |
| `!anokick <online-target> [reason]` | `ano.admin.kick` | Disconnect and publish a generic server-wide kick notice |
| `!anosilentkick <online-target> [reason]` | `ano.admin.silentkick` | Disconnect without a public notice |

Both commands require an explicit current online session. Player-issued actions use the shared permission, self-target and immunity rules; offline SteamIDs are rejected. Console actions retain the shared console authorization convention. A blank or omitted reason becomes `No reason provided.`, and reasons longer than 512 characters are rejected.

The generic audit records `kick.requested` before invoking the native disconnect and `kick` after it completes. The silent variant uses `kick.silent.requested` and `kick.silent`. If the native action fails, the requested entry remains, but no completed entry or public announcement is written. If the completion audit fails after disconnect, the command reports the partial failure. An ordinary kick announces only after the completion audit; silent kick never announces. Native disconnect, cancellation and the chat notice run on the server thread and must be checked with two real CS2 clients.

## Target rules

Destructive moderation commands intentionally accept one explicit target only.

For online players, target resolution reuses the shared `IPlayerTargetResolver` and authorization reuses `ITargetAuthorizationService`. Exact SteamID64, exact case-insensitive name and unique case-insensitive name prefix are supported. Ambiguous names are rejected rather than guessed.

Multi-target selectors such as `@all`, `@t` and `@ct` are not accepted by these commands.

Offline moderation is supported only with an explicit SteamID64. Player-issued offline actions still require:

- the operation-specific permission,
- an online actor session,
- central immunity approval through `IAuthorizationService.CanTargetAsync`,
- no self-targeting.

Console/server actions have no player actor and therefore do not run player permission or immunity checks.

## Persistence and audit

Successful commands use the shared `IModerationService`; they do not create another moderation store.

Apply operations append durable sanctions and audit data through the transaction-tested moderation repository. Revoke operations preserve sanction history and append an audit entry. Silence is represented as voice + chat sanctions, so later `unmute` and `ungag` can operate independently.

If a revoke command finds no matching active restriction, it fails cleanly instead of reporting success.

## Lifecycle

`ModerationCommandController` owns all eight command registrations. Disposal unregisters all owned commands.

Registration is atomic: if a later command cannot be registered, for example because another module already owns its name, earlier registrations made by that constructor are rolled back before the constructor rethrows.

## Live plugin composition

The module itself intentionally contains no CounterStrikeSharp API calls, but `AnoCorePlugin` now composes it from the shared runtime services.

During runtime activation the plugin creates:

1. `ModerationTargetGateway` from the shared player registry, target resolver, target authorization and authorization service;
2. `ModerationCommandExecutor` from that gateway and the shared moderation service;
3. `ModerationCommandController` before `CounterStrikeCommandBridge` enumerates registered descriptors.

That ordering makes all eight moderation commands visible to the existing CounterStrikeSharp command bridge without a second command system.

The controller is retained for the active runtime lifetime and disposed on unload or activation rollback, which unregisters all owned command descriptors.

## Remaining native enforcement boundary

The commands are live and persist moderation state. Connect-ban enforcement is composed during plugin activation with the shared moderation service and event bus. Already connected players are checked on startup; subsequent connections and reconnections are checked by the subscribed policy. The native adapter rechecks the current session on the server thread, completes only after the disconnect call succeeds, reports a stale session/controller as a failure that can be retried, and cancels queued disconnects when the plugin unloads. Confirm actual disconnect behavior on a disposable CS2 server.

The existing voice/chat enforcement remains separate from generic admin action audit; admin UI remains later #17 work.

Real server acceptance is required before the moderation feature is considered production-ready.
