# Configuration schema migrations

`IConfigStore` remains the compatibility contract for existing flat JSON files. Stores that also implement `IVersionedConfigStore` support explicit schema upgrades.

A versioned file wraps its value with `$schemaVersion`. Existing flat JSON is version 0. Callers provide one exact `ConfigMigration<T>` for every step to the requested version. Results are validated before atomic replacement. Missing or duplicate steps, future versions, malformed envelopes, migration failures and validation errors leave the source untouched. At most 128 steps are accepted.

Modules should request `IVersionedConfigStore` only when upgrades are required. Ordinary `IConfigStore` consumers remain source-compatible. This behavior is engine-independent and makes no native CS2 claim.
