# AnoVeto: three map votes per player

AnoVeto defaults to **up to three distinct map selections per eligible player**. Each selection contributes one vote to its chosen map. Clicking a selected map again deselects it; a fourth selection is rejected. Choices are drafts until explicitly confirmed. This is approval-style map voting, **not a ranked-choice ballot**.

## Operator configuration

The server's existing `plugins/AnoCore/config/anoveto.json` accepts these values:

```json
{
  "Enabled": true,
  "DurationSeconds": 300,
  "MinimumVotes": 1,
  "MaxVotesPerPlayer": 3,
  "TieBreakPolicy": 2
}
```

`MaxVotesPerPlayer` defaults to 3 when omitted and must be between 1 and 8. `MinimumVotes` is **the number of distinct participating SteamIDs**, not the sum of map selections. The HUD accepts one to three maps (or a lower configured maximum). Only confirmed ballots count at expiry or manager close; unconfirmed drafts contribute neither tallies nor quorum.

The active vote pins its map catalog, vote duration, tie-break policy and selection limit. Reloading `anoveto` with `css_anoreloadconfig anoveto` changes **future votes only**. Generic votes continue allowing one choice per player by default.

## In-game behavior

- Authorized host: `!anoveto create` opens the existing map-pool picker and presets; `!anoveto random` starts the public vote directly with eight random maps.
- Eligible participant: click one to three different maps; clicking a selected card again removes it. The third click does not submit automatically.
- Press **Auswahl bestätigen** to submit the entire ballot atomically and close your HUD. An empty ballot cannot be confirmed. Confirmed ballots are final; they cannot be changed or submitted again.
- Closing releases the cursor while preserving the current draft. Reopen with `!anoveto` to continue selecting or inspect your confirmed choices and the names/count of players who already voted. The roster updates for everyone currently viewing the HUD after confirmation; other players' map choices remain private.
- The shared vote finishes after **every eligible player confirms**, even with only one or two maps each, at timeout, or on manager close. MinimumVotes still counts unique players.
- The winning map uses the **total count of distinct map selections** received. The configured tie-break policy is applied normally; no more than one map transition is initiated by one finalized vote.
- Reconnecting with the same SteamID retains previous choices, including the selection limit.

## Deployment and acceptance

The backend and ANOMEME layout/style changes must be installed from matching builds. Back up the existing configuration. Replace published AnoCore plugin program files while preserving `config/core.json` and all other server configs. Rebuild and re-upload `anomeme_ui` through Steam Workshop Tools for the new selected-card highlighting and footer text; preserve its published Workshop item ID and existing MultiAddonManager mount settings.

On the DatHost test server, verify with at least two human players: confirm separate one-, two- and three-map ballots; deselect misclicks; reject a fourth map and empty confirmation; close/reopen drafts and confirmed ballots; show voter names and the correct count; reject repeat confirmation and post-confirm edits; reconnect with the same SteamID; clear drafts at cancellation/new vote; exclude unconfirmed drafts at timeout. Repeat with quorum > 1, ties, a MatchZy roster and a tournament-triggered veto. Check roster readability with a full server, no duplicate map changes and released mouse input on close/finalization.

Automated unit/contract tests must pass before deployment. They do **not** prove Steam/CS2 HUD rendering; record the exact server plugin and Workshop addon versions during manual acceptance.
