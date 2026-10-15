# Versioned stake-relative slot payouts (#520)

New controlled Biggest Boy rounds use `anocore-biggest-boy-controlled-v6`;
new fruit rounds use `anocore-book-controlled-v3`. Saved v5/v2 rounds resume
with their original paytables and probabilities. Historical physical v4 and
older rules remain frozen. Wallet balances and booked payouts are not migrated.

Every number below is multiplied by **one tenth of the total stake**, for
3 / 4 / 5 consecutive symbols from the left on a payline. Only the largest
win on each line pays; the ten different lines add. Wild substitutions and
free-spin expansion follow the existing rules.

| Biggest Boy symbol | 3 | 4 | 5 |
|---|---:|---:|---:|
| Döner | 5 | 15 | 50 |
| Bier | 4 | 12 | 40 |
| Zigarette | 3 | 10 | 30 |
| Butter | 2 | 8 | 25 |
| Shot | 2 | 6 | 20 |
| Trichter | 1 | 5 | 15 |
| G / O | 1 | 3 | 10 |
| A / T | 1 | 2 | 8 |

| Fruit symbol | 3 | 4 | 5 |
|---|---:|---:|---:|
| Cherry | 2 | 5 | 10 |
| Lemon | 1 | 4 | 8 |
| Plum | 2 | 5 | 10 |
| Orange | 3 | 8 | 15 |
| Grape | 4 | 10 | 20 |
| Bell | 5 | 15 | 30 |
| Seven | 8 | 25 | 50 |

At a total stake of 100, three Döner on one line return 50 (net -50),
five return 500 (net +400), and a Döner full screen returns 5,000.
Three sevens on one line return 80; five return 500. Both slots have a
50-times-stake maximum per spin and conservative 1,550-times-stake maximum
round exposure (one paid spin plus up to 30 free spins).

These are moderate original tables, not a clone or a certified RTP model.
Published Book-style rules confirm the line-stake calculation and ordering
of low/high symbols, but their much higher top multipliers rely on different
reel probabilities. Reference: Play’n GO GRS0595-3, pages 4–6:
https://cnsicdn.kubdev.com/common-content/help/CNSI/game-documents/EN-Book-of-Dead.pdf

The existing `WinPercent` is a paid-spin **net win** probability, and a
positive-payout probability during free spins. Small returns below or equal
to the paid stake can occur outside that event. `FreeSpinPercent` stays
independent, including retriggers. `FullScreenPercent` is conditional on a
winning nonbonus spin. Rates other than those audited change the return.

New-rule bounded generation constructs an ordinary winning line when random
retries fail; it no longer uses a uniform full-screen win as its ordinary-win
fallback. Legacy fallback behaviour remains unchanged for accepted rounds.

Run the reproducible audit (includes every awarded free spin):

```sh
dotnet run --project tools/AnoCore.ControlledSlotAudit -c Release -- 100000 20261015
```

The audit uses stake 100 and the requested 18/8/1 Biggest Boy and 18/5/1 fruit
controls. It reports empirical return, approximate 95% intervals, paid net-win
frequency, bonus frequency and largest observed round. A sample is not an exact
long-run RTP guarantee. Full-screen expansions can arise naturally in bonuses.

Deployment: update the server plugin. Compile/upload the included Workshop UI
to show the new and historical rule-specific paytables correctly. Existing
images, sounds, welcome grid and animation timings are preserved.

Native acceptance: open each game, inspect its paytable, play at low stake,
reopen a saved historical bonus and confirm its original table, finish the
bonus and verify one settlement. Verify sound toggle, refresh and stake lock.

## Audit result (100,000 completed paid rounds per game)

Seed 20261015, stake 100, .NET 10. The measured returns including all bonuses
were **82.2049%** for Biggest Boy (approximate 95% interval 80.17–84.24%)
and **90.9526%** for fruit (88.95–92.95%). Paid net-win frequencies were
17.972% / 17.986%; bonus frequencies 8.020% / 4.937%. Largest observed complete
round returns were 116.7× / 117× stake. These are conditional on the exact
18/8/1 and 18/5/1 settings and are not certified theoretical RTPs.
