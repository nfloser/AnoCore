# Connect-ban enforcement

AnoCore enforces persisted connect restrictions through an engine-independent lifecycle policy before the CounterStrikeSharp disconnect adapter is introduced.

## Flow

`ConnectBanEnforcement` subscribes to:

- `PlayerConnectedEvent`
- `PlayerReconnectedEvent`
- `PlayerDisconnectedEvent`

For each connected or reconnected session it queries the shared `IModerationService` at the current UTC instant.

If the returned moderation state contains `ModerationRestriction.Connect`, enforcement invokes the shared disconnect boundary:

`IPlayerDisconnectAction.DisconnectAsync(PlayerSnapshot, reason, cancellationToken)`

The policy does not call CounterStrikeSharp directly.

## Session semantics

Disconnect attempts are deduplicated by `PlayerSessionId`.

- duplicate connect/reconnect events for the same active banned session do not trigger multiple disconnect actions;
- a new reconnect session is evaluated independently;
- a failed disconnect removes the in-progress session marker so the operation can be retried;
- a normal `PlayerDisconnectedEvent` releases the tracked session to avoid unbounded lifecycle state.

Expired or revoked connect restrictions do not trigger the disconnect boundary.

## Failure and cancellation

Database/moderation lookups and disconnect actions are asynchronous.

Cancellation is propagated. A failed or cancelled disconnect is not treated as successfully enforced, so a later lifecycle event may retry it.

Disposing the enforcement object unsubscribes all lifecycle handlers and clears tracked sessions.

## Native integration boundary

This package intentionally does not implement the CounterStrikeSharp disconnect call.

The next small #17 package should provide the native `IPlayerDisconnectAction` adapter and compose `ConnectBanEnforcement` from the plugin using the existing shared event bus and moderation service.

Native engine calls must be marshalled onto the CS2 server update thread where required by CounterStrikeSharp.

## Acceptance evidence

Automated tests cover:

- active connect restriction;
- allow path for inactive/expired/revoked state;
- duplicate lifecycle event suppression;
- new reconnect session;
- failed disconnect retry;
- cancellation;
- disconnect lifecycle cleanup;
- disposal/unsubscription.

Real-server verification remains required after the native adapter is wired.
