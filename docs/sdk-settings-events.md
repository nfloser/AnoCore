# Player settings change events

A module can resolve `IPlayerSettingsService`, `IPlayerSettingsResetService`, `IPlayerSettingsBatchService` and `IAnoEventBus` from the runtime service provider. It can subscribe to `PlayerSettingChangedEvent` and dispose the subscription on unload. The event includes the `PlayerId`, the validated setting name and `Set` or `Reset` kind. It never includes the stored value or default value.

`SetAsync` publishes once after the data store accepts the write. `ResetAsync` publishes only when a stored entry was deleted; resetting a missing key is silent. Failed writes and cancellations before storage do not publish. Event delivery starts after the write and uses a separate cancellation scope: an observer failure cannot roll back a committed write and is reported through the optional `onEventFailure` diagnostic callback. Other observers still receive the event through the shared bus. Delivery does not provide transactionality or a durable queue, and concurrent writes may be observed out of order.

`IPlayerSettingsBatchService.SetManyAsync<T>` accepts up to 64 same-type updates, validates the full batch and commits it in one database transaction. Empty batches are no-ops. After commit, it publishes the existing value-free `PlayerSettingChangedEvent` once per key in input order. Cancellation, validation or storage failure publishes nothing and leaves all previous values intact. The optional persistence capability is `IModuleDataBatchStore`.

`IPlayerSettingsResetService.ResetAllAsync` deletes the complete `player.<SteamID>.setting.` namespace in one store operation and returns the exact removed-entry count. An effective reset publishes one value-free `PlayerSettingsResetEvent` containing only the player and count. A no-op, cancellation or failed delete emits nothing. The prefix capability is a separate `IModuleDataPrefixStore`, so existing third-party `IModuleDataStore` implementations remain source-compatible; they must add the optional capability before bulk reset is available.

The event is available in the runtime service composition; existing settings callers can keep using the original constructor parameters. The package does not provide a settings catalog, automatic menu, offline event replay, public API endpoints or complete SDK compatibility; those remain part of the functional acceptance matrix.


## Module-owned toggle catalog

Modules resolve `IPlayerToggleCatalog` from the runtime service provider and register a `PlayerToggleSetting` with their `ModuleId`. The descriptor contains a typed `PlayerSettingKey<bool>`, a short printable label and an optional description. Registration is limited to 64 settings; duplicate keys are rejected across all modules. `GetAll()` returns an immutable key-sorted snapshot. Dispose the registration handle on unload, or call `UnregisterAll(owner)` when unloading a module. A stale handle cannot remove a later registration for the same key.

The catalog holds metadata only. A module reads or changes a particular player's value through `IPlayerSettingsService`, which emits the post-commit event described above. Registering an option does not set a player value. A native menu, command access, localization and settings discovery for existing modules remain separate work; the catalog by itself is not a player-facing UI.
