# Player settings change events

A module can resolve `IPlayerSettingsService`, `IPlayerSettingsResetService`, `IPlayerSettingsBatchService` and `IAnoEventBus` from the runtime service provider. It can subscribe to `PlayerSettingChangedEvent` and dispose the subscription on unload. The event includes the `PlayerId`, the validated setting name and `Set` or `Reset` kind. It never includes the stored value or default value.

`SetAsync` publishes once after the data store accepts the write. `ResetAsync` publishes only when a stored entry was deleted; resetting a missing key is silent. Failed writes and cancellations before storage do not publish. Event delivery starts after the write and uses a separate cancellation scope: an observer failure cannot roll back a committed write and is reported through the optional `onEventFailure` diagnostic callback. Other observers still receive the event through the shared bus. Delivery does not provide transactionality or a durable queue, and concurrent writes may be observed out of order.

`IPlayerSettingsBatchService.SetManyAsync<T>` accepts up to 64 same-type updates, validates the full batch and commits it in one database transaction. Empty batches are no-ops. After commit, it publishes the existing value-free `PlayerSettingChangedEvent` once per key in input order. Cancellation, validation or storage failure publishes nothing and leaves all previous values intact. The optional persistence capability is `IModuleDataBatchStore`.

`IPlayerSettingsResetService.ResetAllAsync` deletes the complete `player.<SteamID>.setting.` namespace in one store operation and returns the exact removed-entry count. An effective reset publishes one value-free `PlayerSettingsResetEvent` containing only the player and count. A no-op, cancellation or failed delete emits nothing. The prefix capability is a separate `IModuleDataPrefixStore`, so existing third-party `IModuleDataStore` implementations remain source-compatible; they must add the optional capability before bulk reset is available.

The event is available in the runtime service composition; existing settings callers can keep using the original constructor parameters. The package does not provide a settings catalog, automatic menu, offline event replay, public API endpoints or complete SDK compatibility; those remain part of the functional acceptance matrix.


## Module-owned toggle catalog

Modules resolve `IPlayerToggleCatalog` from the runtime service provider and register a `PlayerToggleSetting` with their `ModuleId`. The descriptor contains a typed `PlayerSettingKey<bool>`, a short printable label and an optional description. Registration is limited to 64 settings; duplicate keys are rejected across all modules. `GetAll()` returns an immutable key-sorted snapshot. Dispose the registration handle on unload, or call `UnregisterAll(owner)` when unloading a module. A stale handle cannot remove a later registration for the same key.

The catalog holds metadata only. A module reads or changes a particular player's value through `IPlayerSettingsService`, which emits the post-commit event described above. Registering an option does not set a player value. A native menu, command access, localization and settings discovery for existing modules remain separate work; the catalog by itself is not a player-facing UI.


## Module-owned cleanup

During `InitializeAsync`, a module can pass disposable registrations to `IAnoModuleContext.Own(resource)`. The runtime creates a fresh scope for every load attempt and disposes owned resources in reverse registration order after `ShutdownAsync`. Cleanup also runs when initialization or shutdown fails, and cleanup failures are retained in the module snapshot diagnostics. Typical owned resources are toggle-catalog handles, event subscriptions and command registrations.

Modules should still make `ShutdownAsync` safe after partial initialization. The host invokes it before owned resources are released, allowing the module to stop work while its subscriptions are still valid. Custom contexts remain source-compatible through the default ownership implementation, but only the runtime host guarantees automatic cleanup.


## Player commands

A connected player uses `anosettings [page]` to see up to three registered keys with effective `on`/`off` values. `anotoggle <key> on|off|default` writes or resets only that player's setting. `default` removes the stored override and exposes the descriptor's configured default. Unknown keys and actions, out-of-range pages, server console and disconnected players are rejected. The command module is registered with runtime startup and disposed with it. Registered modules should only expose player-editable choices in this catalog; it does not provide an administrative override or permission-scoped setting discovery.

The command handlers and MariaDB persistence are tested. Native CS2 command dispatch, chat output and reconnect timing still require live acceptance. No settings menu is provided yet; the module list remains empty until feature modules register their own options.


## Player settings menu

`anosettingsmenu [page]` opens the shared player menu with at most three registered options per page. Each option shows the effective value and offers a toggle and a separate reset to its configured default. Page navigation is bounded by the current catalog snapshot. A choice is checked against the current connected session and the same active descriptor before persistence. Menu generations have distinct option IDs, so a delayed callback from an older native view cannot select a replacement option with the same label.

Matching disconnect/reconnect events remove the old session's menu registration; unload disposes menu and event registrations. The plugin presents the menu after command dispatch through the existing native presenter. Until modules register choices, the menu reports that no settings are available. CS2/DatHost menu rendering, click order, command dispatch and unload remain live acceptance gates. This menu currently uses the shared CenterHtml presenter; migrating it to the CustomHud renderer requires its separate live acceptance.
