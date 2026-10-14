# AnoVeto: three map votes per player

AnoVeto defaults to **up to three distinct map selections per eligible player**. Each selection contributes one vote to its chosen map. Selecting the same map again does not add a vote, and a fourth selection is rejected. This is approval-style map voting, **not a ranked-choice ballot**.

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

`MaxVotesPerPlayer` defaults to 3 when omitted and must be between 1 and 8. `MinimumVotes` is **the number of distinct participating SteamIDs**, not the sum of map selections. A user can choose fewer than three maps; votes already cast still count when the vote expires or an authorized manager closes it.

The active vote pins its map catalog, vote duration, tie-break policy and selection limit. Reloading `anoveto` with `css_anoreloadconfig anoveto` changes **future votes only**. Generic votes continue allowing one choice per player by default.

## In-game behavior

- Authorized host: `!anoveto create`; the same eight-map vote HUD opens to eligible players.
- Eligible participant: select up to three **different** maps. After the first and second selection, the HUD remains open and shows `1/3` or `2/3`, with already chosen map cards highlighted.
- Third distinct selection completes that participant's ballot and hides their HUD.
- Clicking a selected map again is ignored for vote counting. Closing the window does not cancel or erase submitted choices; reopen via `!anoveto` while the vote remains active.
- The shared vote finishes when **every eligible SteamID has submitted all three selections**, the configured duration expires, or an authorized manager closes it. Players who submit fewer choices do not block expiry.
- The winning map uses the **total count of distinct map selections** received. The configured tie-break policy is applied normally; no more than one map transition is initiated by one finalized vote.
- Reconnecting with the same SteamID retains previous choices, including the selection limit.

## Deployment and acceptance

The backend and ANOMEME layout/style changes must be installed from matching builds. Back up the existing configuration. Replace published AnoCore plugin program files while preserving `config/core.json` and all other server configs. Rebuild and re-upload `anomeme_ui` through Steam Workshop Tools for the new selected-card highlighting and footer text; preserve its published Workshop item ID and existing MultiAddonManager mount settings.

On the DatHost test server, verify with at least two connected human players: both can independently choose three unique maps, duplicates do not count, an incomplete ballot stays open, third choice closes the voter's HUD, reconnect does not reset choices, and a vote does not finish merely because each player chose one map. Repeat with timeout, quorum > 1, manual cancel, tie handling, a loaded MatchZy roster and a tournament-triggered veto. Confirm no duplicate map changes or stuck mouse input.

Automated unit/contract tests must pass before deployment. They do **not** prove Steam/CS2 HUD rendering; record the exact server plugin and Workshop addon versions during manual acceptance.
