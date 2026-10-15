# Casino money display and capacity

Balances, load amounts and winnings use German compact units: Tsd., Mio., Mrd., Bio., Brd., Trill., with up to two decimal places. Amounts below 1,000 remain exact. Rounding promotes 999,999 to 1 Mio. Stakes, including Blackjack hand stakes, always show all digits. Display rounding never affects settlement or the ledger.

The shared wallet ceiling is now 1,000,000,000,000,000,000 virtual units, including reserved maximum payouts. Both the legacy wallet and multi-action round store use the same limit. Reservations can still reject a new round near the ceiling. Game payout, starting stake, credit and per-transfer limits are unchanged; this is not unlimited betting.

Existing account and reservation columns already use signed BIGINT, and C# amounts use long. No schema migration or account reset is required. Replace the complete server plugin DLL package from this branch during a normal server maintenance restart; preserve configuration and database. Existing Workshop UI accepts server-generated strings, so no Workshop rebuild/upload is required. No UI assets changed.

Acceptance: verify lobby and all six games, compact account/bank/wins/free-spin totals, exact high stakes and Blackjack hand stakes; check return/load and reconnect. Native CS2 acceptance must be recorded separately from automated CI.
