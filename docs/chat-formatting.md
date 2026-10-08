# Shared chat formatting (development)

Plugin startup creates and validates the engine-independent formatter from `config/chat-format.json`; a formatting failure is isolated from other modules. The formatter combines trusted templates with the shared asynchronous placeholder registry. Its defaults are:

```json
{
  "PublicTemplate": "{rank.tag} {player.name}: {message}",
  "TeamTemplate": "(TEAM) {rank.tag} {player.name}: {message}",
  "RankColor": "None",
  "NameColor": "None",
  "MessageColor": "None"
}
```

Each template must contain `{player.name}` and `{message}` exactly once, contain only printable characters and be at most 256 characters. Other registered placeholders may be used. `{chat.tag}` is the shared prioritized tag slot; `{rank.tag}` remains available and legacy templates using it are routed through the shared slot when a chat-tag provider is registered. An empty selected tag intentionally produces no tag.

`RankColor`, `NameColor` and `MessageColor` accept only `None`, `Team` or the documented native names: `Default`, `White`, `DarkRed`, `LightPurple`, `Green`, `Olive`, `Lime`, `Red`, `Grey`, `Yellow`, `Silver`, `LightBlue`, `DarkBlue`, `Purple`, `LightRed` and `Orange`. Existing installations remain uncolored by default. `Team` resolves to the sender's current team color. Every colored rank, name or message slot is followed by the native default reset so colors cannot bleed into later text.

## Tag ownership and priority

Optional native CSS role prefixes are configured separately in `role-chat-tags.json`;
see [role-chat-tags.md](role-chat-tags.md) for the backup-matched example, Founder
override, file migration and authorization boundaries. They precede the formatted
line and do not replace or grant access to the shared `chat.tag` slot.

Modules register `chat.tag` providers with distinct integer priorities. The highest-priority provider that returns a value owns the displayed tag. Returning `null` means “not applicable” and falls back to the next provider; returning an empty string intentionally suppresses all lower-priority tags. The rank module owns priority `0`, leaving positive priorities for administrative, permission or temporary tags and negative priorities for fallbacks.

Equal priorities are rejected, as is mixing exclusive and prioritized ownership for one placeholder. Disposing a registration or unloading its owner reveals the next applicable provider without disturbing other owners. Tag providers are evaluated while warming a session snapshot, never in the synchronous native chat hook.

## Player-selectable tags

`config/chat-tags.json` defines up to 32 choices with `Id`, `Text` (at most 24 printable characters) and an `ano.*` `Permission`. The default is an empty choice list. For example:

```json
{
  "Tags": [
    { "Id": "staff", "Text": "[Staff]", "Permission": "ano.chat.staff" }
  ]
}
```

A connected player uses `anotags` to list only permitted choices, `anosettag staff` to select one, and `anocleartag` to return to the rank tag. `anochatmenu [page]` opens the same choice in the shared player menu with up to five eligible tags per page, a rank-tag reset option and bounded navigation. Menu clicks are bound to the displayed native menu instance and exact registered menu definition; an old callback cannot select an option from a replacement with the same ID. They check the current session and permission again before changing settings. Matching disconnect and reconnect events remove only that session's menu registration. Choices are persisted in the shared typed player settings. The selected tag provider has priority 100; it returns `null` when the choice is missing, removed or permission is denied, revealing the rank tag at priority 0. Tag text is validated against native controls and braces. Successful changes refresh the current session's prepared chat format. Authorization reload starts invalidation and refresh for every connected player without waiting for one slow player before processing the next. A failed refresh cannot keep a revoked tag in native chat. The native `say` hook reads only these prepared snapshots.

## Safe evaluation order

`ChatMessageFormatter` first resolves trusted shared placeholders with the sender's `PlayerId` in the `player` context. It substitutes the player name and message only afterwards. Braces in a player's name or message are therefore literal data and cannot invoke another placeholder.

Player names are limited to 48 characters and messages to 256 characters. Control characters are replaced with spaces, so names and messages cannot inject native color codes. A missing or blank display name falls back to SteamID64.

## Warmed session snapshots

Plugin activation creates a `ChatFormatSnapshotLifecycle` and bootstraps already-connected players. Connect/reconnect warms prepared public and team formats, name updates and persisted rank-score changes refresh them, and disconnect removes only the matching `PlayerSessionId`. In-flight work from an old session cannot publish over a newer reconnect. Bootstrap failures are isolated per player. Unload cancels work, unsubscribes lifecycle handlers and clears all entries before rank placeholder registrations are removed.

`TryFormat` validates both SteamID and session identity, then performs only bounded synchronous message substitution. Missing state fails closed and performs no placeholder or database work. Combat events refresh every distinct affected player's current session even when rank-transition messages are disabled; committed administrative give/take/set/reset operations refresh the target even for changes within one rank. Refresh failures are presentation-only and cannot change the already persisted result. Same-session refreshes keep the last valid format until replacement; a monotonic generation prevents an older in-flight refresh from overwriting newer rank data.

## Native adapter

The CounterStrikeSharp `say` / `say_team` pre-listener now routes ordinary human-player messages through `NativeChatRouter`. Chat commands beginning with `!` or `/` pass through unchanged so CounterStrikeSharp can dispatch registered `css_` commands. For ordinary messages the router applies the synchronous moderation decision, validates the tracked sender session, formats from the warmed snapshot and returns an explicit recipient set. Public messages target all tracked connected humans; team messages target only tracked connected players whose current team matches the sender.

The adapter suppresses the original chat command and prints the one formatted line to the selected valid native clients. A blocked sender or missing/stale warmed snapshot is suppressed without a database lookup or fallback leak. If chat formatting did not compose at startup, allowed native chat continues unchanged while moderation remains active.

This is compile- and unit-tested routing behavior. Real `say` argument shape, listener ordering, public/team visibility, named colors and rank-tag presentation still require the documented CS2/DatHost live acceptance before the draft stack can be merged.
