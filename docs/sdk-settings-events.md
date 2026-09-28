# Player settings change events

A module can resolve `IPlayerSettingsService` and `IAnoEventBus` from the runtime service provider. It can subscribe to `PlayerSettingChangedEvent` and dispose the subscription on unload. The event includes the `PlayerId`, the validated setting name and `Set` or `Reset` kind. It never includes the stored value or default value.

`SetAsync` publishes once after the data store accepts the write. `ResetAsync` publishes only when a stored entry was deleted; resetting a missing key is silent. Failed writes and cancellations before storage do not publish. Event delivery starts after the write and uses a separate cancellation scope: an observer failure cannot roll back a committed write and is reported through the optional `onEventFailure` diagnostic callback. Other observers still receive the event through the shared bus. Delivery does not provide transactionality or a durable queue, and concurrent writes may be observed out of order.

The event is available in the runtime service composition; existing settings callers can keep using the original constructor parameters. The package does not provide a settings catalog, automatic menu, offline event replay, public API endpoints or complete SDK compatibility; those remain part of the functional acceptance matrix.
