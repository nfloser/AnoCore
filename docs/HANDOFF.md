# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged.
- Persistent moderation #41 / PR #42 is merged.
- Persistent moderation admin commands #43 / PR #44 are merged.
- Connect-ban lifecycle policy #45 / PR #46 is merged.
- Native CounterStrike disconnect adapter #47 / PR #48 is merged.
- Existing-session connect-ban bootstrap #49 / PR #50 is merged.
- Live moderation command composition #51 / PR #52 is merged in main at `26ffa548092cfb816acf22d09470432085efbec2`.
- Active communication-enforcement prerequisite: issue #53 / PR #54 / branch `feature/53-moderation-snapshots`.
- Independent CustomHud/AnoVeto PR #40 passed CI #158 after integrating shared foundations and remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and remaining #17 voice/chat/tags/messaging/admin-UI packages remain separate.

## Known-good #53 checkpoint

- Snapshot implementation head before main integration: `f64a768dbd748eeb9c721dfbeb2c97722cbcb71b`.
- CI run: `35455020890` (#201) passed build, tests, formatting, publish, package validation and artifact upload.
- Current integrated head includes main merge `05bf79062cf0abd0a384513c5fff99e8d620219b` plus documentation.
- Require final exact-head CI before review/merge.

## Implemented in #53 / PR #54

- New engine-independent `IModerationSnapshotProvider`.
- `ModerationService` is the single shared implementation of both persisted moderation operations and snapshots.
- Cache miss is explicit: `TryGetRestrictions` returns false until that player has been loaded through `GetStateAsync`.
- Cached entries retain immutable sanctions, so temporary expiry is evaluated locally without another database read.
- Apply updates an already-loaded snapshot only after persistence succeeds.
- Revoke replaces matching cached sanctions only after persistence succeeds and preserves unrelated restrictions.
- Failed or cancelled mutations leave loaded snapshots unchanged.
- Per-player state loads/mutations are serialized through 64 fixed async lock stripes to avoid stale load-vs-mutation races without an unbounded lock dictionary.
- `InvalidateAsync` uses the same SteamID stripe and removes an individual snapshot only after any in-flight load/mutation for that stripe has completed, so invalidation wins load races.
- `RuntimeServices` exposes the exact same `ModerationService` instance as `IModerationService` and `IModerationSnapshotProvider`.
- Tests cover cache miss, load, expiry boundary, apply/revoke updates, partial restriction preservation, invalidation/load races, failure/cancellation and shared runtime composition.
- Current main was merged into the branch without losing #51 live moderation command composition.

## Why this exists

Native chat and especially voice callbacks must not perform a MariaDB query for every engine event/packet. The database remains authoritative, while native high-frequency adapters can use the warmed shared snapshot for synchronous allow/deny decisions.

A native consumer must treat a snapshot miss as “not loaded”, not as proof that the player is unrestricted. It should warm state through `GetStateAsync` at an appropriate lifecycle boundary before relying on the fast path.

## Next steps

1. Run final exact-head CI after documentation, review PR #54 and merge with expected-head SHA if green.
2. Add a small #17 native communication-enforcement package consuming `IModerationSnapshotProvider`: warm/invalidate state on lifecycle and apply Chat/Voice restrictions in engine adapters.
3. Keep the voice/chat native adapters separate if the CounterStrikeSharp hooks require different cleanup or validation behavior.
4. Keep PR #40 draft until real CS2/DatHost CustomHud acceptance is recorded.
5. Keep #36 Extended Commands separate and reuse shared authorization/targeting/moderation.
6. Preserve small commits, exact-head CI and handoff notes before context boundaries.

## Integration rules

Do not create duplicate moderation caches, stores or permission systems. Warm snapshots asynchronously outside high-frequency engine callbacks, then use `IModerationSnapshotProvider` synchronously. Engine mutations after async work must be marshalled onto the server update thread. Preserve unload/hot-reload cleanup and fail safely on stale player sessions.

License, NOTICE and source provenance must remain intact.
