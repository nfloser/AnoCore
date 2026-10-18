# AnoRoulette native table and ball motion

Issue #528, stacked on Blackjack PR #527 including its text-binding correction.
The European Roulette game and all existing 49 bet choices are unchanged. All
money is virtual; no real-money settlement is provided.

## Presentation

The wheel graphic and betting board stay stationary. A separate ivory ball moves
on a bounded coordinate path, independent of the graphic. The 500px wheel uses
its existing image: zero at twelve o'clock, then the canonical clockwise order.
The ball's center starts at radius 228px and finishes at radius 148px, inside the
booked pocket. The image is never rotated or reoriented to force the result.

RouletteBallMotion maps elapsed time and the booked number to one of 888 positions
(148 angles × six radial bands). There is a 200ms preparation phase, then two
full circuits plus the remaining arc with quadratic deceleration and an inward
finish. The ball lands at 3.6s; the result holds until controls reopen at 4s.
The existing 50ms presenter tick changes only the previous/new ball position
classes; 50ms native transform transitions smooth the small arcs. Finite translate
coordinates avoid rotation-angle normalization and animation-name replay issues.
A delayed tick still resolves exactly to the booked pocket.

The shell follows Blackjack: green felt, muted gold, readable counters and stake,
classic three-row number layout, zero and all outside bets, gold selected-bet
border, result number and color, separate gross return and net change. MIN/MAX,
half/double and existing +/- controls adjust the next stake within configured
limits; MAX also respects allocated funds. Stakes remain exact, other amounts
use compact German currency. Only one selected wager is placed per round.

All server-updated labels bind `{s:text}`, including the new result ribbon.
No result number or payout credit is displayed during the moving phase. The
wallet itself is already settled; animation never draws or mutates money. Net
change uses the original booked stake, even if the next stake is changed. Old
history receipts do not carry the original stake, so restored history shows the
number and gross return without inventing a net value.

## Recovery and packaging

Close, back, refresh and reconnect never reroll a receipt or book an additional
stake. A historical result restores its exact ball position. Reset/disconnect
clears the previous pose before reconstructing native state. Existing wallet
receipt IDs, revision/transaction guards, RNG, payout rules and SQL schema are
unchanged. Book/Biggest Boy/Blackjack assets and resources are retained.

Generated native XML/CSS are committed. Reproduce them with:

```bash
python tools/generate_roulette_ui.py
```

Install the matching complete server plugin package, preserving config/database,
and compile the matching UI with the existing build script. For one Windows
client, close CS2 and run from the extracted package root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui -InstallLocalClient
```

Restart the testserver after replacing the plugin. Start CS2, connect and open
`!anocasino` → Roulette. Other players need the updated existing Workshop addon.
A UI-only update cannot provide the new ball presenter. Roulette's own maximum
still comes from `CasinoMaximumStakes.Roulette`; this does not change defaults.

## Verification and native acceptance

Deterministic tests cover all 37 pockets from all 148 starts, bounded movement,
preparation, final poses, multiple successive spins, exact-once bookkeeping,
close/refresh/reset/history restore, original net stake and high quick stakes.
Operator tests verify 888 coordinates against the wheel's pocket geometry, all
49 clickable bets, every text binding, inert button children and generator
reproducibility. The former rotation-keyframe contracts were replaced because
rotating the wheel is explicitly the reported behavior being removed.

Before merge/release, compile with Valve resourcecompiler and verify in real CS2:
stationary board/wheel, visible ball throughout, smooth deceleration and inward
landing matching the displayed/ledger number; multiple consecutive spins and
both winning/losing bets; every bet and stake shortcut; readable text at supported
resolutions; close/reopen, reconnect/map change, two-client isolation and no
Panorama errors. Source tests and CI do not establish native visual correctness.
