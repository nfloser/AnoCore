# Shared chat formatting (development)

Plugin startup creates and validates the engine-independent formatter from `config/chat-format.json`; a formatting failure is isolated from other modules. The formatter combines trusted templates with the shared asynchronous placeholder registry. Its defaults are:

```json
{
  "PublicTemplate": "{rank.tag} {player.name}: {message}",
  "TeamTemplate": "(TEAM) {rank.tag} {player.name}: {message}"
}
```

Each template must contain `{player.name}` and `{message}` exactly once, contain only printable characters and be at most 256 characters. Other registered placeholders, including `{rank.tag}`, may be used. An empty configured rank tag intentionally produces no tag.

## Safe evaluation order

`ChatMessageFormatter` first resolves trusted shared placeholders with the sender's `PlayerId` in the `player` context. It substitutes the player name and message only afterwards. Braces in a player's name or message are therefore literal data and cannot invoke another placeholder.

Player names are limited to 48 characters and messages to 256 characters. Control characters are replaced with spaces. A missing or blank display name falls back to SteamID64.

## Warmed session snapshots

Plugin activation creates a `ChatFormatSnapshotLifecycle` and bootstraps already-connected players. Connect/reconnect warms prepared public and team formats, name updates refresh them, and disconnect removes only the matching `PlayerSessionId`. In-flight work from an old session cannot publish over a newer reconnect. Bootstrap failures are isolated per player. Unload cancels work, unsubscribes lifecycle handlers and clears all entries before rank placeholder registrations are removed.

`TryFormat` validates both SteamID and session identity, then performs only bounded synchronous message substitution. Missing state fails closed and performs no placeholder or database work.

## Native adapter

The CounterStrikeSharp `say` / `say_team` pre-listener now routes ordinary human-player messages through `NativeChatRouter`. Chat commands beginning with `!` or `/` pass through unchanged so CounterStrikeSharp can dispatch registered `css_` commands. For ordinary messages the router applies the synchronous moderation decision, validates the tracked sender session, formats from the warmed snapshot and returns an explicit recipient set. Public messages target all tracked connected humans; team messages target only tracked connected players whose current team matches the sender.

The adapter suppresses the original chat command and prints the one formatted line to the selected valid native clients. A blocked sender or missing/stale warmed snapshot is suppressed without a database lookup or fallback leak. If chat formatting did not compose at startup, allowed native chat continues unchanged while moderation remains active.

This is compile- and unit-tested routing behavior. Real `say` argument shape, listener ordering, public/team visibility, colors and rank-tag presentation still require the documented CS2/DatHost live acceptance before the draft stack can be merged.
