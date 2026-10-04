# Shared messaging

AnoCore exposes engine-independent messaging contracts through `AnoCore.Abstractions.Messaging` and a shared runtime `IMessageService`.

## Targets and channels

A message targets exactly one audience:

- one player, optionally pinned to a `PlayerSessionId` so reconnects cannot receive stale output;
- one concrete team;
- all currently connected tracked players.

The shared channels are chat, center text and center HTML. Modules must not depend on CounterStrikeSharp to request these outputs.

## Priority and duration

Chat is immediate and does not own a display lease.

Timed center and center-HTML messages own one logical slot per target/channel. A message with a lower priority than the active lease is suppressed. Equal or higher priority replaces it. Expiry clears the native output only when the expiring lease is still current, so an older timer cannot erase a newer message.

Text, priority and duration are bounded by `MessageRequest`.

## Localization and placeholders

Messaging transports already-rendered text. Modules should resolve localization and trusted placeholders before dispatching and then pass the bounded result to `IMessageService`. This keeps catalogs and placeholder providers independent from engine delivery while giving every module one native output path.

## Native adapter

`CounterStrikeMessageTransport` is attached during plugin activation. It resolves recipients from the shared player registry on the server thread and maps channels to CounterStrikeSharp chat/center APIs. Player targets may carry a session id to reject output queued for a stale connection.

Automated tests cover the engine-independent lifecycle. Actual chat/center rendering, duration semantics and map/reload behavior remain part of the disposable CS2/DatHost acceptance gate in #23.
