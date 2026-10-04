# Player playtime (development)

A separate Stats module records sessions by SteamID64 and random session ID. Its MariaDB ledger stores connection start, the last accounted checkpoint and optional close time. Replayed connect and heartbeat callbacks do not duplicate time; delayed events for an older session cannot update a newer session. A closed session never advances again. A reconnect closes the previous session and opens a new one.

The plugin records disconnects and checkpoints connected players every five seconds. At startup it opens already connected players. On unload it schedules a final checkpoint; an abrupt host crash can lose time since the last successful checkpoint, bounded by the heartbeat interval when the database is responsive. Offline time is never inferred across a restart. Durable writes may lag during database outages; errors appear in runtime logs.

`IPlaytimeRepository.ReadAsync(playerId, utcDay)` returns cumulative accounted time and the portion overlapping the requested UTC day. Sessions spanning midnight are clipped at UTC day boundaries. Connected players can use `css_anoplaytime` to checkpoint and read their own total and today's UTC time; the server console cannot read personal playtime. `css_anotoptime [page]` displays five entries per page, sorted by cumulative accounted time descending and SteamID64 ascending on ties. Pages 1–1000 are supported, zero-time sessions are omitted, and the command is available to the console and players. Entries show the last saved profile name alongside SteamID64 where present, or SteamID64 alone. Richer menu navigation remains future work. This is a foundation for time statistics and toplists. Player menus, broader ranking/statistics policy and full #18 acceptance remain separate work.

Live acceptance: test normal join/disconnect, rapid reconnect, hot reload, database outage/recovery, UTC midnight and abrupt restart with two clients. Record actual loss window and database/version/plugin commit before shipping.


## Team and alive-state breakdown

Repositories may opt into `IPlaytimeStateRepository` without changing existing
`IPlaytimeRepository` consumers. The MySQL runtime implementation records durable
team/alive-state segments alongside the existing total-session ledger.

Player updates checkpoint the previously active segment at the update timestamp and
then open the new state. Heartbeats advance the current segment, reconnect/disconnect
close the old session, and out-of-order or already-closed session writes are ignored.
The session checkpoint and segment mutation share one database transaction so a
partial state transition cannot leave total and breakdown playtime disagreeing.

`!anoplaytime` keeps the existing total/today output and, when the repository exposes
the optional capability, appends at most eight non-zero state buckets such as
`T/alive` or `CT/dead`. UTC-day clipping follows the same boundary rules as total
playtime. Historical sessions recorded before migration 008 remain part of totals but
are intentionally not retroactively assigned to a team/alive bucket.

Native acceptance still has to verify that CounterStrikeSharp player update timing
matches the intended team/death transitions on a disposable server.
