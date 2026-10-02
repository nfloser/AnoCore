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
