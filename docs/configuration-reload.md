# Module configuration reloads

`IConfigReloadRegistry` exposes bounded, module-owned reload registrations. A registration has a stable descriptor and a typed `Current` value. Reloads are serialized per registration. A loaded candidate replaces `Current` only after validation succeeds and the registration is still alive.

Failed, cancelled or invalid loads retain the last accepted value. Disposing the registration removes it and prevents delayed work from publishing. Modules should give the handle to `IAnoModuleContext.Own(...)`.

Operators with `ano.core.reload` can use `anoconfigs` to inspect the stable registered name/owner list and `anoreloadconfig <name>` to reload exactly one entry. The server console may use both commands. Unknown names and loader or validation failures return the normal bounded command failure and do not replace the last accepted value.

This registry supplies atomic in-process publication for each registration. Concrete modules still decide how files are loaded and must adopt the registry before their configuration appears in `anoconfigs`. Cross-registration reloads are intentionally not implied: an operator names one independently validated configuration, avoiding partially applied bulk reloads.

## AnoVeto

When AnoVeto starts enabled, it registers `anoveto` and `maps` with the shared reload registry. A successful reload is picked up when the next vote is created. A vote that is already open keeps the policy and eight-map snapshot it started with.

`Enabled` remains a startup switch. Reloading an active AnoVeto instance with `Enabled: false` is rejected, so disabling or enabling the module still requires a plugin restart. Invalid settings or maps leave the last accepted runtime value in place.

## Operator reload matrix

| Configuration / state | Supported application boundary |
| --- | --- |
| `chat-format` | `anoreloadconfig chat-format`: templates and colors publish together; stale prepared chat is immediately rejected and connected-player snapshots rebuild on the next one-second tick. Ordinary chat remains available while warming. |
| `chat-tags` | `anoreloadconfig chat-tags`: validated tag IDs/text/permissions replace one compiled catalog. Selection and old menu callbacks check the current catalog. Removed selections fall back to rank tags; cached chat is immediately rejected, then rebuilt on the next one-second tick. |
| `playtime-notifications` | `anoreloadconfig playtime-notifications`: enabled/interval apply at the next durable checkpoint. Existing last-attempt timestamps and player opt-outs remain; no totals change. |
| `anoveto`, `maps` | Active AnoVeto registration only; next vote gets the accepted snapshot. Current vote stays pinned; activation requires restart. |
| `gameplay-xp` | Active module only; rewards, caps and policy version apply to the next checkpoint. Enabled, checkpoint interval and earn-start changes are rejected and require restart. Earned grants are never rescored. |
| Authorization roles, players, timed VIP | Database-backed: role commands commit and refresh automatically; `anoreloadauth` handles externally edited authorization state. UTC expiry is enforced immediately. Not a JSON config reload. |
| `core` | Restart: database/connection composition, protected controls, presentation and native feature switches. |
| `modules` | Restart: trusted assembly paths, hashes, activation and API compatibility. |
| `management` | Restart: authentication/capability, transport and provider composition. Sidecar environment settings require sidecar restart. |
| `combat-recording` | Restart: shot/hit recording switches, bounded queue, batch mode/size/interval and shutdown deadline. Existing ledgers remain. |
| `ranks`, `gameplay-stats` | Restart: source mode, scoring policy, thresholds, native hooks and presentation policy. Existing ledgers/totals are retained; startup does not rewrite earned history. |
| `achievements`, `challenges`, `seasons` | Restart: definition catalogs, versioned rewards and XP curve/scheduling. Do not edit old immutable reward versions to rescore earned grants; use new versions for changed definitions. |
| `moderation-webhooks` and webhook environment variables | Restart: opt-in destination, approved hosts, privacy and delivery bounds are pinned for the pump lifetime. |
| `tournament-match` | Validated on explicit match load/map-policy application. Active match recovery remains pinned to stored match state; no generic hot reload. |
| Player settings/storage | Existing typed persistence commands and APIs apply per-player changes; not startup JSON configuration. |

`anoconfigs` lists actual live registrations, not every file. Restart-only files are intentionally absent. Failed JSON, invalid candidates, cancellation and unload retain the last accepted runtime policy. There is no bulk reload or watcher. Native acceptance: reload each registered policy with two connected clients, verify new chat/tag/notification behavior, invalid-candidate retention, active-vote isolation and unload/restart cleanup.
