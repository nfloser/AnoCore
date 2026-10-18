# AnoBlackjack

Native Panorama table in the existing `anomeme_ui` addon. Issue #526 builds on
`feat/524-compact-casino-money` (PR #525); do not replace this integration with main.

## Rules and money

One freshly shuffled complete 52-card deck per round, drawn without replacement.
Aces count as 1 or 11. Dealer checks for natural blackjack on the initial deal,
hits below 17 and stands on soft 17. Natural blackjack returns 2.5 times stake
(including the original stake); ordinary win returns twice stake; push returns
stake; bust/loss returns zero. Split 21 is an ordinary win, never a natural.

Hit takes one card; Stand ends the current hand; Double costs another hand stake,
takes exactly one card and stands. Equal ranks may split once into two hands.
Play the left hand until standing, 21 or bust, then the right. Split aces take
one card per hand and stand automatically. Double after a non-ace split is allowed.
No insurance, surrender or re-split controls are offered.

The configured Blackjack maximum applies to the initial stake. Split and Double
can raise total committed stake to four times the initial stake. Additional
stakes require allocated funds; the durable store reserves maximum payout and
uses action IDs/revisions to prevent duplicate debit or settlement.

## Presentation

Green felt, muted gold table edge, larger cards and a bright active-hand border.
Hand summaries explicitly distinguish waiting, active, standing, bust, win,
blackjack and push. The result ribbon shows net change, gross return and total
stake so a returned stake cannot be mistaken for profit. Stake values stay exact;
balances/results use German compact currency units. Dealer hole rank, suit and
total stay hidden until settlement.

The style extension is restricted to `.view-blackjack`. Legacy Book stylesheet
bytes before the extension remain hash locked; XML, art and slot rules are unchanged.

## Acceptance and delivery

Automated regressions cover split turn order, natural versus split 21, both
naturals, bust priority, dealer soft 17, exact stakes, concealed cards and refresh.
The full Python operator suite passes: 97 tests, 18 environment skips.
Release solution build passed with zero warnings/errors. Regular MSTest Arcade
run: 176 passed, 19 database-dependent skipped. Broader solution run: 1,265 passed,
203 environment-dependent skipped, excluding ManagementPipeBridgeTests because
local Unix sockets are restricted. Python operator run: 97 total, 18 skipped,
no failures. Formatting was attempted but the Roslyn workspace failed with a
named-pipe SocketException (permission denied); whitespace checks passed.
MariaDB integration and exact-head CI remain pending.
No native acceptance or production-ready claim is made.

Both the server plugin and matching compiled Workshop UI need updating for the
new design. Existing build scripts compile the updated rounds stylesheet.
Verify in CS2 before merge: table fits at supported resolutions, both split hands
and all actions are clickable, cards animate/read correctly, repeated rounds,
close/reopen and reconnect restore the same cards, rapid duplicate clicks never
charge twice, low funds reject Double/Split without changing the hand, and wallet
receipts match displayed gross/net results. Check Panorama parser logs. Use two
clients to verify isolation. Keep config/database; no schema migration or reset.
