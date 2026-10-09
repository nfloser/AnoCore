# Map catalog and generic voting

Issue #19 provides the shared, CounterStrikeSharp-independent map and voting primitives used by AnoVeto and future modules.

## Map catalog

`MapDefinition` separates the human display name from the engine map ID and optional Steam Workshop ID. Catalog construction fails fast on duplicate display names, map IDs or workshop IDs. Map IDs use a restricted character set to prevent command injection at the game adapter boundary.

`MapCatalogLoader` reads the `maps` configuration through `IConfigStore`, so server operators can maintain map metadata without recompiling modules. Lookups by display name and engine map ID are case-insensitive; `All` is deterministically sorted by display name.

When AnoVeto is active, `maps` is also registered with the shared configuration reload service. Reloading it changes the catalog used by the next vote; an already-open vote keeps its original eight-map snapshot.

`CounterStrikeMapChanger` is the only CS2-specific map-loading component. Workshop maps use `host_workshop_map`; regular maps use `changelevel`. Core voting code never executes server commands directly.

## Generic voting

`VoteService` owns vote sessions. A `VoteDefinition` declares an `ano.*` vote ID, display title, ordered options, an explicit eligible SteamID population, duration, minimum-vote quorum and tie-break policy.

Create, close and cancel operations require `ano.vote.manage`. Casting is restricted to the eligible `PlayerId` set and each SteamID can vote once. Because ballots key on `PlayerId`, reconnecting creates no second voting identity.

Expired votes are finalized through `FinalizeExpired(now)` or when a late cast is attempted. `OptionOrder` is the deterministic tie-break policy: the earliest tied option in the definition wins. `NoWinner` can instead leave tied votes without a winner. `Random` (numeric value `2`) chooses uniformly among the options tied for the highest vote count. The result is drawn once and retained; options with fewer votes cannot win. Quorum failure never invents a winner.

## Menu integration

`VoteMenuFactory` turns a logical vote snapshot into an AnoCore menu definition. Feature modules register/open that menu using the shared issue #16 menu service and route selections back into `VoteService`.

## AnoVeto dependency

AnoVeto (#20) should consume this infrastructure rather than implement its own ballot engine. It may randomly select its eight candidate maps, but eligibility, one-vote enforcement, timeout, tallying and result semantics belong here.

## Runtime verification

Unit/CI tests verify deterministic core behavior. A real CS2 server test is still required before release to verify actual `changelevel`/Workshop command behavior and menu presentation; native execution is not claimed by CI.

## AnoVeto server settings

Edit `game/csgo/addons/counterstrikesharp/plugins/AnoCore/config/anoveto.json`
and set `TieBreakPolicy` to `2` for random ties (`0`: first tied option;
`1`: no winner on a tie). Keep the other settings and any configuration envelope.
`DurationSeconds` controls the deadline (default 30), and `MinimumVotes`
controls quorum (default 1). Run `css_anoreloadconfig anoveto` in the server
console after saving. The next vote uses the new policy; an open vote keeps
its original policy. This requires the updated server plugin, but no Workshop
addon rebuild or upload.
