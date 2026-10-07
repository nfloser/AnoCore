# Module SDK

`AnoCore.Abstractions` is the supported compile-time contract for modules that run on AnoCore. External modules should not reference `AnoCore.Runtime`, `AnoCore.Plugin`, CounterStrikeSharp or MySqlConnector directly unless they intentionally implement an engine/infrastructure adapter outside the supported module boundary.

## Get the package

CI packs a prerelease `AnoCore.Abstractions` NuGet package into the `AnoCore-development` artifact under `sdk/`. The package is not published to a public registry while AnoCore remains in private prerelease development.

After extracting the CI artifact:

```bash
dotnet add package AnoCore.Abstractions --version 0.1.0-alpha.1 --source ./sdk
```

A module project should have a normal package reference and no project reference back into the AnoCore repository:

```xml
<ItemGroup>
  <PackageReference Include="AnoCore.Abstractions" Version="0.1.0-alpha.1" />
</ItemGroup>
```

## Minimal module

```csharp
using AnoCore.Abstractions.Modules;

public sealed class ExampleModule : IAnoModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        new ModuleId("example"),
        "Example",
        "1.0.0",
        "Minimal external module",
        AnoCoreApi.CurrentLevel);

    public Task InitializeAsync(
        IAnoModuleContext context,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

If initialization creates disposable registrations, transfer them to the module scope through `context.Own(...)`. The host releases owned resources in reverse order during normal unload and failed startup rollback.

## API compatibility

`ModuleDescriptor.MinimumApiLevel` declares the oldest AnoCore module API required by a module.

- The original four-argument descriptor constructor stays at `AnoCoreApi.BaselineLevel`.
- Use the explicit API-level constructor only when the module depends on contracts introduced at that level.
- `ModuleHost` checks the requirement before calling `InitializeAsync`.
- A host rejects unsupported older/future levels before the module can register resources.
- API level is a compatibility gate. Optional behavior should still be discovered through service contracts where possible.

Until AnoCore reaches its first stable release, package versions are prerelease and public contracts may still evolve. A module should pin the exact SDK version used for its build and declare its required API level.

## Package boundary

CI verifies both the compiled assembly references and the generated package. The SDK package must contain:

- `AnoCore.Abstractions.dll`;
- generated XML API documentation;
- `LICENSE.md`;
- `NOTICE.md`;
- the package README.

The package inspection fails if its NuGet metadata gains a dependency on AnoCore Runtime/Plugin, CounterStrikeSharp, MySqlConnector or any other package dependency. This protects the module-facing boundary independently of source layout.

## Host notification events (API level 2)

The SDK exposes `PlayerChatAcceptedEvent` and `CoreUnloadingEvent` through the
shared `IAnoEventBus`. Declare `MinimumApiLevel = 2` when requiring these contracts;
baseline level 1 modules remain supported by the host.

Accepted chat is an immutable observation after AnoCore moderation/routing accepts
connected-player input. Commands, empty input, blocked senders and failed formatting
are excluded. It captures the sender snapshot/session, public/team channel, UTC
acceptance time and up to 1024 characters of unformatted input. Native pass-through
chat also emits it when a connected sender is known. It does not confirm client
delivery and cannot mutate or cancel routing. Observer errors are isolated.

Core unload starts one `CoreUnloadingEvent` per plugin load before teardown, with
hot-reload reason and UTC time, even if runtime startup did not finish. This is an
advisory observation: native unload cannot await asynchronous module callbacks.
Use `context.Own(...)` and `ShutdownAsync` for required cleanup; do not rely on this
event to finish asynchronous database work before resources disappear.

Both publishers use best-effort notifications without persistence or replay.
Subscribers should capture facts and return/yield promptly; synchronous subscriber
work still runs on the publishing thread. Asynchronous callbacks must revalidate
current session/lifetime before using host services and avoid native API access
without the host's appropriate thread dispatch.

## Configured external modules

Place trusted managed SDK-only assemblies in `plugins/AnoCore/modules/` and list
exact .dll basenames in `config/modules.json`, for example:

```json
{ "Assemblies": ["Example.Module.dll"] }
```

The default list is empty. The host never scans arbitrary DLLs or downloads code.
It accepts at most 32 unique bounded filenames, 20 MiB per assembly, 32 public
parameterless `IAnoModule` classes per assembly and a bounded total discovery set.
Paths, links, malformed assemblies, duplicate module identities and references to
Runtime/Plugin/optional implementation modules/CounterStrikeSharp/MySqlConnector
are rejected. Modules initialize in deterministic assembly/type order after native
services activate, through the same SDK assembly identity and public service provider.
Failures isolate the extension and are visible as `ready with module errors` plus
module state/logs; they do not disable built-in features. Cooperative startup uses
a 30-second cancellation window. This is a trusted-code extension mechanism, not a
sandbox: install only reviewed binaries. Package binary changes require a process
restart; a plugin hot reload may reuse the already loaded assembly identity.

Module commands are reconciled to native bindings on the world-update thread,
including additions, removals and replacements. Shared menu open/close changes are
also dispatched to native presentation with captured session checks and monotonic
open revisions, so repeated notifications do not reopen a user-closed menu. Use `context.Own` for command/event/
settings/menu/config registrations. Host shutdown immediately releases these owned
handles, cancels pending initialization, rejects new loads and completes shutdown
in reverse load order, isolating failures. Native unload does not block on async
shutdown; extensions must not depend on disposed host services after yielding.

Acceptance: install one SDK-only module, execute its command, inspect host/module
status, trigger a failed initialization, then reconnect/hot reload/unload. Verify
no duplicate native command or stale registration survives. Automated tests load a
separately compiled SDK-only fixture and cover real ownership/path/lifecycle edges.
