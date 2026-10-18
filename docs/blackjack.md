# AnoBlackjack

Dedicated native Panorama table in `anomeme_ui`, based on PR #525. Issue #526.
All amounts are virtual; there is no real-money settlement.

## Published rules

New rounds use `anoblackjack-v2`: one freshly shuffled complete 52-card deck,
without replacement. Aces count as 1 or 11. Dealer stands on every 17, including
soft 17, and draws below 17. An initial dealer ten-value card is checked for
blackjack immediately; an Ace first offers insurance, then checks the hole card.
No extra stakes are exposed to an unchecked dealer blackjack.

- Hit takes one card. Stand ends the current hand. At 21 or bust it ends automatically.
- Natural blackjack returns 2.5 times stake including the stake; ordinary wins
  return twice stake; push returns stake; bust/loss returns zero.
- Double costs one additional hand stake, deals exactly one card and stands.
  Allowed on two-card hands, including after non-ace splits.
- Equal ranks split into two hands. Re-split up to four total hands; play left to
  right, completing each hand before moving on. Split aces receive one card each,
  then stand; they cannot re-split/double. Split 21 is an ordinary win.
- Insurance is optional against an open Ace. It costs half the initial stake.
  Dealer natural returns three times the insurance stake (2:1 profit plus stake);
  otherwise insurance loses independently of the main hand. Decline costs nothing.
- Even Money is offered with a player natural against an Ace: settle immediately
  for twice the original stake, without an extra insurance debit.
- Late surrender is offered after the dealer peek on the original unsplit
  two-card hand, before Hit/Double/Split/Stand. Half the initial stake is returned.

Existing `anoblackjack-v1` JSON remains supported without migration: immediate
peek, maximum two hands, original action set and original 8x reservation.
Version strings stay unchanged on resumed rounds. New options apply only to v2.

## Wallet and recovery

Initial stakes use configured Blackjack limits and multiples of 50. MIN, MAX,
half and double shortcuts adjust the next stake without booking money. MAX is
bounded by allocated funds and configuration. Stake fields always show exact
German digit grouping; balances/results use compact units.

Four doubled hands cost at most 8x the initial stake; insurance adds up to 0.5x.
A conservative 17x gross payout reservation bounds new rounds. The theoretical
maximum main-hand gross return is 16x, and winning insurance requires a dealer
natural which settles before splitting. Insurance debit and payout are persisted
separately; TotalStake includes insurance. All actions go through the existing
account/session locks, expected revisions, action ledger and atomic settlement.
A failed debit leaves cards/state unchanged. Close/reopen and reconnect restore
committed cards rather than drawing again.

## Native presentation and delivery

Separate `ano_casino_blackjack.xml/.css` and HUD identity preserve all locked Book
and Biggest Boy resources byte for byte. Green felt, muted gold borders, 26px
card ranks/suits, 28px balances, 18–20px action/hand text. Four hands fit a 2x2 grid.
Short card rows are centered; more than eight cards use a bounded left-aligned
fan. The result ribbon distinguishes net change from gross return and total
stake. Insurance and Even Money have separate labels; only affordable legal
optional controls are shown. A scrollable rules sheet provides full explanations.

Both native build scripts include the new view. Update the complete server
plugin and compile/upload the matching `anomeme_ui` addon. A plugin-only update
cannot deliver the new layout or its controls. Keep configuration/database;
no account reset or SQL schema migration is needed.

## Validation and acceptance

Tests cover deterministic split order/re-splits, insurance win/loss and both
naturals, Even Money, late surrender, legacy restore, serialization, 500 seeded
complete rounds with conserved cards/exposure bounds, exact wallet debit,
insufficient-funds rollback, presenter availability/concealment/quick stakes,
native resource geometry/build contracts and database concurrent insurance replay.

Local Release build and non-database tests pass. Database tests run in CI;
formatting whitespace passes locally, semantic formatter needs permitted named
pipes. Exact final counts and CI status are recorded in HANDOFF.md and the PR.

Native acceptance remains required before merge/release: Valve resourcecompiler
without parser errors; supported resolutions/readability and all buttons; two,
three and four hands; insurance/decline/Even Money/surrender; repeated rounds;
close/reopen and reconnect with unchanged cards; rapid duplicate clicks; low
funds on Insurance/Double/Split; receipts matching gross/net results; two-client
isolation. Automated source tests do not establish native visual correctness.

Rule references (conceptual, no copied assets/code):
- https://bicyclecards.com/how-to-play/blackjack/
- https://www.pacodeandbulletin.gov/secure/pacode/data/058/chapter633a/s633a.9.html
