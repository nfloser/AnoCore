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
