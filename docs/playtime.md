# Player playtime (development)

A separate Stats module records sessions by SteamID64 and random session ID. Its MariaDB ledger stores connection start, the last accounted checkpoint and optional close time. Replayed connect and heartbeat callbacks do not duplicate time; delayed events for an older session cannot update a newer session. A closed session never advances again. A reconnect closes the previous session and opens a new one.

The plugin records disconnects and checkpoints connected players every five seconds. At startup it opens already connected players. On unload it schedules a final checkpoint; an abrupt host crash can lose time since the last successful checkpoint, bounded by the heartbeat interval when the database is responsive. Offline time is never inferred across a restart. Durable writes may lag during database outages; errors appear in runtime logs.

`IPlaytimeRepository.ReadAsync(playerId, utcDay)` returns cumulative accounted time and the portion overlapping the requested UTC day. Sessions spanning midnight are clipped at UTC day boundaries. This is a foundation for time statistics and toplists. Team/alive breakdown, player menus, ranking, notifications and full #18 acceptance remain separate work.

Live acceptance: test normal join/disconnect, rapid reconnect, hot reload, database outage/recovery, UTC midnight and abrupt restart with two clients. Record actual loss window and database/version/plugin commit before shipping.
