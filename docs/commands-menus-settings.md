# Commands, menus and player settings

Issue #16 establishes the shared interaction layer used by Ano modules.

## Command namespace

All AnoCore-owned command names and aliases must start with `ano`. CounterStrikeSharp bindings prepend `css_`, which means a logical command such as `anoveto` is registered natively as `css_anoveto` and is available to players as `!anoveto` or `/anoveto`.

`CommandRegistry` owns parsing, alias resolution and authorization. A permission is checked before the handler is invoked. Server-console callers have no `PlayerId` and are not subjected to player permission checks; individual commands may still reject console use in their handler.

Quoted arguments are parsed by the runtime so commands can safely receive values containing whitespace. Command descriptors may declare typed arguments (`String`, `Int32`, `UInt64`, `Boolean`), required/optional ordering and help descriptions. The registry validates and converts these arguments before invoking the handler. Invalid or missing values produce a usage response without executing feature code. `CommandContext.Get<T>(name)` provides the validated typed value.

`CommandDescriptor.Usage` is generated from argument metadata unless a custom usage string is supplied. `IAnoCommandRegistry.GetCommands()` exposes immutable descriptor snapshots for future `!anohelp` and web/API surfaces.

Handler exceptions are converted into a failed `CommandResult` with a fixed, user-safe message instead of escaping into the game callback. Exception messages may contain database details, paths or configuration values and are never returned to players or the server console. `HandlerFailed` remains available to code consuming the result. Native dispatch failures log the logical command name without raw arguments. Operational handler failure diagnostics still require a separate, appropriately redacted logging path.

Command registrations are owned by a `ModuleId`. Disposing a registration or calling `UnregisterAll(owner)` removes the command and all aliases, preventing hot-reload leaks.

## Menus

`MenuService` is CounterStrikeSharp-independent. Modules register a `MenuDefinition` under an `ano.*` ID, open it for a `PlayerId` and process selections through async callbacks.

Selections are one-shot by default: the runtime closes the logical menu before invoking the callback. This protects against duplicate key presses and re-entrant callbacks. An option may explicitly set `keepOpen: true` when repeated interaction is intended.

`CounterStrikeMenuPresenter` renders the current logical menu through CounterStrikeSharp `CenterHtmlMenu`, including the framework's built-in pagination and navigation. Module unload removes owned definitions and closes affected logical sessions.

## Player settings

`PlayerSettingsService` provides typed `PlayerSettingKey<T>` values with deterministic defaults. Values are stored through `IModuleDataStore` under the dedicated `settings` namespace using keys derived from SteamID64 and the validated setting name.

A setting name may contain lowercase letters, digits, `.`, `_` and `-`. Path-like or uppercase names are rejected so settings cannot escape their logical namespace.

Example:

```csharp
var compactChat = new PlayerSettingKey<bool>("chat.compact", false);
var current = await settings.GetAsync(playerId, compactChat);
await settings.SetAsync(playerId, compactChat, true);
```

Persistence therefore survives reconnects and process restarts whenever the configured `IModuleDataStore` is backed by the database provider from issue #14.

## Runtime boundary

CounterStrikeSharp-specific command and menu code lives only in `AnoCore.Plugin`. The contracts and runtime implementations remain independently unit-testable and reusable by later modules such as Admin, Stats, AnoVeto and Tournament.
