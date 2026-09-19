# Connect-ban enforcement

AnoCore enforces persisted connect restrictions through an engine-independent lifecycle policy and a thin CounterStrikeSharp disconnect adapter.

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

## Native disconnect adapter

`CounterStrikePlayerDisconnectAction` implements `IPlayerDisconnectAction` in the plugin layer.

It schedules the native operation through `Server.NextWorldUpdate`, then revalidates the shared registry before touching the engine. The disconnect is skipped when:

- the operation token was cancelled after scheduling;
- the player is no longer tracked/connected;
- the tracked `PlayerSessionId` no longer matches the session that was originally banned.

That session guard prevents a delayed native callback from disconnecting a newly reconnected session that happens to use the same SteamID64.

For a still-current session, the adapter resolves the live non-bot/non-HLTV `CCSPlayerController` by SteamID64 and calls:

`Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED)`

CounterStrikeSharp's disconnect API accepts a network-disconnection enum, not AnoCore's free-form moderation reason, so the durable reason remains in AnoCore's moderation/audit history.

## Remaining composition boundary

The native adapter is present but is not yet instantiated from `AnoCorePlugin`, because draft PR #40 concurrently owns that composition file. A follow-up integration package must reconcile current main with #40 and construct `ConnectBanEnforcement` using the shared registry/event bus/moderation service plus this adapter.

Native engine calls remain marshalled onto the CS2 server update thread.

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

CI verifies the adapter compiles/packages against CounterStrikeSharp API 374. Real-server verification remains required after plugin composition is wired.
