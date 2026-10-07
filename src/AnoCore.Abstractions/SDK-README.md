# AnoCore module SDK

`AnoCore.Abstractions` is the supported compile-time surface for external AnoCore modules. Module projects should reference this package rather than `AnoCore.Runtime`, `AnoCore.Plugin`, CounterStrikeSharp or persistence assemblies.

## Consume the CI package

The SDK is currently produced as a prerelease package inside the `AnoCore-development` GitHub Actions artifact. It is not published to a public NuGet feed yet.

After extracting the artifact, point NuGet at the directory containing `AnoCore.Abstractions.0.1.0-alpha.1.nupkg`:

```bash
dotnet add package AnoCore.Abstractions --version 0.1.0-alpha.1 --source ./sdk
```

A module project should only need the SDK package:

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

Use `IAnoModuleContext.Own(...)` for disposable registrations created during initialization so the host can unwind them on unload and failed startup.

## Compatibility

`ModuleDescriptor.MinimumApiLevel` is checked before initialization. The four-argument descriptor constructor keeps the stable baseline API level for compatibility. Use the explicit API-level constructor only when the module requires contracts introduced at that level. A host that does not support the requested level rejects the module before its initialization code runs.

API levels are a compatibility gate, not feature detection. Prefer resolving optional service contracts when a feature can be discovered dynamically.

The package contains the generated XML documentation plus the repository license, notice and this README. Preserve applicable attribution when redistributing derived work.


## Management integration

The SDK exposes the transport-neutral `AnoCore.Abstractions.Management` contracts.
Modules can optionally resolve `IManagementCapabilityRegistry` from
`IAnoModuleContext.Services` and register a bounded management capability that is
owned by the module lifetime:

```csharp
using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;

var registry = context.Services.GetService(typeof(IManagementCapabilityRegistry))
    as IManagementCapabilityRegistry;

if (registry is not null)
{
    context.Own(registry.Register(
        Descriptor.Id,
        new ManagementCapabilityDescriptor(
            new ManagementCapabilityId("example.refresh"),
            "Refresh example module state.",
            ManagementScope.ManageModules,
            ManagementOperationClass.Privileged),
        (_, _, _) => ValueTask.FromResult(
            ManagementOperationResult.Ok("Example state refreshed."))));
}
```

Management contracts do not start a network listener and do not expose arbitrary
server-command execution. Authentication, scopes, rate limiting and audit behavior
remain host responsibilities implemented through the shared management core.


## Shared messaging

Modules can optionally resolve `IMessageService` and request chat, center or center-HTML output without referencing CounterStrikeSharp:

```csharp
using AnoCore.Abstractions.Messaging;

var messages = context.Services.GetService(typeof(IMessageService)) as IMessageService;
if (messages is not null)
{
    await messages.SendAsync(
        new MessageRequest(
            MessageTarget.ForPlayer(player.Id, player.SessionId),
            MessageChannel.Chat,
            "[ANO] Example module ready."),
        cancellationToken);
}
```

Timed center messages also accept a bounded priority and duration. The host suppresses lower-priority output while a higher-priority lease is active. Resolve localization and trusted placeholders before dispatching so the transport remains independent of catalogs and formatting providers.

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
