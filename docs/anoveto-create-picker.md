# AnoVeto create picker – implementation handoff

Tracking: #444; existing prerequisite design #374 and #375. This branch provides only the **pure eight-map draft model**, not the finished `!anoveto create` interface.

## Intended command and window flow

The `!anoveto create` command opens a session-owned ANOMEME dialog with three actions: **Create Veto**, **Random Veto**, **Use Veto**. The existing immediate random start must stay available under a distinct `!anoveto random` fast path during migration. Only an authorized player may manage or publish a vote.

- **Create Veto**: show sorted eligible catalog, map thumbnail/name, scroll controls and clear eight-map selection progress. On confirmation, pass a snapshot of exactly eight valid MapIds to the existing AnoVeto coordinator, not raw client-visible labels.
- **Random Veto**: start an independent random eight-map draw from the same eligible catalog.
- **Use Veto**: load owner-specific named eight-map presets from existing MariaDB; support preview, launch, rename, duplicate, edit and delete with transaction/version checks.
- Existing active voting HUD must remain independent; the three unique equal-weight selections from PR #383 belong to the ballot, **not** the eight-map picker.

## Current foundation

`AnoVetoMapDraft` filters and pages an eligible map catalog and rejects invalid IDs or ninth selections. A confirmed selection returns an isolated ordered snapshot. This class does **not** persist presets, authorize players, create votes, instantiate the CustomHud or handle reconnection. No server deployment or runtime behavior is claimed.

## Input acceptance tests required before removing page buttons

Test in CS2 with a rebuilt, matching `anomeme_ui` addon and two clients:

1. The window opens once, survives re-open, closes without stealing movement input, and does not modify active voting.
2. Mouse wheel scrolls a long catalog. Click/drag scrollbar, first/last item and navigation after map changes. Keep Prev/Next fallback until all pass.
3. Keyboard probe with real Panorama focus: type letters, Backspace, Escape, reconnect and ordinary chat/console commands. If unreliable, omit the text input; use alphabetical/category buttons and a separately implemented chat search.
4. Thumbnail beside each map, unique identity, missing-image fallback and no stale textures after closing.
5. Correct eight-map ownership and database preset CRUD; invalid/disabled map prompts replacement, never silent random substitution.
6. Confirm correct 3-selection public vote behavior from PR #383 and Casino controls from the exact same server/plugin+Workshop revision.

## Combined Casino test delivery

Do not publish this branch on its own. Integrate after validating #383, #374/#375 and the Casino stack #421/#438/#442; build one exact package with `BUILD-COMMIT.txt`, run all existing .NET/MariaDB/operator/PowerShell/Panorama CI checks and then verify native CS2. Preserve `core.json`, server database and Workshop item `3815363712`.
