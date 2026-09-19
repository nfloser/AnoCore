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

## Integration boundary

This module intentionally contains no CounterStrikeSharp API calls.

The next native #17 integration packages must:

1. instantiate the admin module from the shared runtime services;
2. deny/disconnect clients with an active `Connect` restriction;
3. enforce `Voice` restrictions in the voice path;
4. enforce `Chat` restrictions in the chat path;
5. refresh enforcement immediately after command state changes where required;
6. preserve all existing centralized target, permission, immunity and audit behavior.

Real server acceptance is required before the moderation feature is considered production-ready.
