# Shared chat formatting (development)

The engine-independent formatter loads `config/chat-format.json` and combines trusted templates with the shared asynchronous placeholder registry. Its defaults are:

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

## Native boundary

This component does not intercept or rebroadcast CS2 chat. The existing native moderation listener is synchronous and must not perform a MariaDB lookup on the chat hot path. A future native formatting adapter must maintain a session-safe warmed snapshot, apply the synchronous moderation decision first, preserve public/team routing and command behavior, then broadcast the already formatted message on the server thread.

Until that adapter is implemented and observed on CS2/DatHost, this package is only the tested common formatting policy. It does not establish native rank-tag display.
