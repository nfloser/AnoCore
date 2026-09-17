# AnoCore Development Handoff

This file is updated at package boundaries so work can continue without reconstructing state.

## Current workstream

- Issue: #20 — Implement AnoVeto map vote module
- Branch: `feature/20-anoveto`
- Pull request: #31 (draft)
- Original branch base: `7d8333d61aa47106db39bed293a7123661d10aae`
- Latest functional commit before this checkpoint: `4e191886603598da1a7de6222d31ce9ec117b59b`

## Completed AnoVeto packages

### A — Coordinator contract
Implemented and tested:
- exactly 8 unique maps;
- injectable deterministic random selection;
- reconnect-safe one-vote-per-SteamID behavior;
- deterministic quorum/tie handling;
- timeout finalization;
- cancellation without map change;
- winning map transition emitted at most once.

Known-green checkpoint: `eac10a3c595a963ec34b25218e99d0e60e40ec94`, CI run 59 passed restore, Release build, tests and format.

### B — `!anoveto create`
Implemented:
- command registration;
- eligible online-player collection;
- authorization through the existing vote/permission layer;
- clean unregister on disposal.

Implementation checkpoint: `44266ddd366dcaa9827d32cf2d7b213d67396aa6`, CI run 61 passed fully.

### C — Voting menu
Implemented:
- bare `!anoveto` opens the active vote menu;
- menu contains the 8 selected map display names;
- selection casts through the existing reconnect-safe vote service;
- menu registration is owned/disposed by the AnoVeto controller.

Implementation checkpoint: `eaea54df373ea5dbd2cfffdbcb6bf8e40e3cbb0e`, CI run 64 passed restore, Release build, full tests and format.

### D — status/cancel management
Red tests: `9249e8ab5788a29af572b02819ced8c75430b1cc`.
Implementation: `4e191886603598da1a7de6222d31ce9ec117b59b`.

Implemented:
- `!anoveto status` is available to normal players and reports the active 8-map vote;
- `!anoveto cancel` uses existing manager authorization;
- unauthorized cancellation returns Forbidden and preserves the vote;
- successful cancellation terminates the vote and disposes the registered vote menu, closing open menu sessions;
- bare `!anoveto` fails cleanly after cancellation.

This checkpoint commit exists to trigger and record a fresh CI run for Block D. Do not start Block E until that CI is green.

## Remaining AnoVeto packages

E. CounterStrikeSharp runtime composition and expiry ticking.
F. Documentation and real-CS2 acceptance checklist.
G. Final full CI, self-review, integration with current `main`, fixes and merge.

Each package remains independently committed and tested before the next begins.

## Wider roadmap

Merged foundation includes core/CI, player lifecycle, events/runtime, MariaDB persistence, permissions/immunity, commands/menus/settings, canonical GPLv3, maps and generic voting. Other isolated workstreams remain #17 admin/chat/messaging, #18 stats/ranks/playtime/toplists, #21 tournament, #22 Web/API/server/security/SDK, and #23 migration/package/real-server release acceptance.

## External/release gates

Managed CI does not prove native CounterStrikeSharp/CS2 behavior. Real-server verification remains mandatory for CenterHtml menu rendering, command bridging, event timing, hot reload/unload, timeout behavior and actual map transitions before release readiness is claimed.

## Next exact step

Verify fresh PR CI for this checkpoint. If Block D is green, begin Block E from this known-good head and change only runtime composition/expiry behavior.
