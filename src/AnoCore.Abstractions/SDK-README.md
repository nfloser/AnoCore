# AnoCore module SDK

`AnoCore.Abstractions` is the supported compile-time surface for external AnoCore modules. Module projects should reference this package rather than `AnoCore.Runtime`, `AnoCore.Plugin`, CounterStrikeSharp or persistence assemblies.

## Minimal module

```csharp
using AnoCore.Abstractions.Modules;

public sealed class ExampleModule : IAnoModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        new ModuleId("example"),
        "Example",
        new Version(1, 0, 0));

    public ValueTask InitializeAsync(IAnoModuleContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ShutdownAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
```

Use `IAnoModuleContext.Own(...)` for disposable registrations created during initialization so the host can unwind them on unload and failed startup.

## Compatibility

AnoCore checks a module's declared API-level requirement before initialization. Keep the requirement at the default when using only the baseline contracts; declare a newer API level only when the module actually needs contracts introduced at that level. A host that does not support the requested level rejects the module before its initialization code runs.

This package is currently a prerelease CI artifact. It is not published to a public NuGet registry yet. The package contains the repository license and notice; preserve applicable attribution when redistributing derived work.
