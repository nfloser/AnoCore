# Shared services

AnoCore modules share configuration, localization, placeholder and logging-context primitives without depending on CounterStrikeSharp.

## Configuration

`IConfigStore` exposes typed load/save operations. `JsonConfigStore` stores one validated JSON document per logical name.

- Missing configuration is created from an explicit default factory.
- Defaults and loaded values can be validated by the caller.
- Invalid defaults are never written.
- Malformed JSON fails explicitly instead of silently resetting configuration.
- Names are restricted to letters, numbers, dots, underscores and hyphens; path traversal is rejected.
- Writes use a temporary file in the same directory followed by an atomic replace/move.

Example:

```csharp
var settings = await configs.LoadAsync(
    "veto",
    () => new VetoSettings(8, 30),
    value => value.MapCount == 8 ? [] : ["MapCount must be 8."]);
```

## Localization

`ILocalizationService` resolves keys in this order:

1. requested locale (for example `de-de`);
2. requested language (`de`);
3. configured fallback locale (`en-us`);
4. fallback language (`en`).

Missing keys remain visible as `[[key]]`. Named arguments such as `{player}` are formatted with invariant culture so server output does not depend on the host OS locale.

## Placeholders

`IPlaceholderRegistry` uses owner-bound registrations. Placeholder names are normalized to lowercase and globally unique.

- registration returns an idempotent `IDisposable`;
- `RemoveOwner(ModuleId)` removes every placeholder registered by a module;
- unknown tokens remain unchanged;
- resolver calls are asynchronous and cancellation-aware.

This ownership rule is important for hot unload/reload: a module cannot leave stale placeholder callbacks behind.

## Logging context

AnoCore does not wrap or replace `Microsoft.Extensions.Logging.ILogger`. `AnoLogContext` only provides stable structured fields (`AnoModule`, `SteamId`, `AnoOperation`) that callers can pass into normal logging scopes. This preserves ecosystem compatibility and avoids a parallel logging abstraction.
