using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Modules;

public sealed class ExternalModuleConfiguration
{
    private static readonly Regex Filename = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,123}\\.dll$", RegexOptions.CultureInvariant);
    public List<string> Assemblies { get; set; } = [];

    public static IReadOnlyCollection<string> Validate(ExternalModuleConfiguration value)
    {
        if (value?.Assemblies is null || value.Assemblies.Count > 32)
            return ["External modules require at most 32 explicit assembly filenames."];
        if (value.Assemblies.Any(name => name is null || !Filename.IsMatch(name))
            || value.Assemblies.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Assemblies.Count)
            return ["External module filenames must be unique bounded .dll basenames without paths."];
        return [];
    }
}

public sealed record ExternalModuleLoadResult(string Assembly, string? Type, ModuleId? Id, bool Loaded, string? ErrorType);

public sealed class ExternalModuleCatalog(ModuleHost host, Action<Exception>? reportError = null)
{
    public async Task<IReadOnlyList<ExternalModuleLoadResult>> LoadAsync(string directory,
        ExternalModuleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var errors = ExternalModuleConfiguration.Validate(configuration);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        var results = new List<ExternalModuleLoadResult>();
        if (configuration.Assemblies.Count == 0) return results;
        var root = Path.GetFullPath(directory);
        foreach (var filename in configuration.Assemblies.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("External module directory cannot be a symbolic link.");
                var path = Path.Combine(root, filename);
                var file = new FileInfo(path);
                if (!file.Exists || file.Length is < 1 or > 20 * 1024 * 1024 || file.LinkTarget is not null)
                    throw new InvalidDataException("External module file is missing, linked or exceeds 20 MiB.");
                var identity = AssemblyName.GetAssemblyName(path);
                var loadContext = AssemblyLoadContext.GetLoadContext(typeof(IAnoModule).Assembly) ?? AssemblyLoadContext.Default;
                var existing = loadContext.Assemblies.FirstOrDefault(item => item.GetName().Name == identity.Name);
                if (existing is not null && existing.FullName != identity.FullName)
                    throw new InvalidDataException("A different assembly version is already loaded; restart the process.");
                var assembly = existing ?? loadContext.LoadFromAssemblyPath(path);
                if (assembly.GetReferencedAssemblies().Any(reference => ForbiddenDependency(reference.Name)))
                    throw new InvalidDataException("External SDK modules must not reference AnoCore implementation or engine assemblies.");
                var types = assembly.GetExportedTypes().Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                    && typeof(IAnoModule).IsAssignableFrom(type)).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
                if (types.Length is < 1 or > 32 || results.Count + types.Length > 128)
                    throw new InvalidDataException("An assembly must expose 1-32 SDK modules, at most 128 across configured assemblies.");
                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ModuleId? id = null;
                    try
                    {
                        if (type.GetConstructor(Type.EmptyTypes) is null)
                            throw new InvalidDataException("External SDK modules require a public parameterless constructor.");
                        var module = (IAnoModule)Activator.CreateInstance(type)!;
                        id = module.Descriptor.Id;
                        await host.LoadAsync(module, cancellationToken).ConfigureAwait(false);
                        results.Add(new(filename, type.FullName, id, true, null));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        Report(exception);
                        results.Add(new(filename, type.FullName, id, false, exception.GetType().Name));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Report(exception);
                results.Add(new(filename, null, null, false, exception.GetType().Name));
            }
        }
        return results;
    }

    private static bool ForbiddenDependency(string? name)
        => name is "AnoCore.Runtime" or "AnoCore.Plugin" or "MySqlConnector"
            || (name?.StartsWith("AnoCore.Modules.", StringComparison.Ordinal) ?? false)
            || (name?.StartsWith("CounterStrikeSharp", StringComparison.Ordinal) ?? false);

    private void Report(Exception exception)
    {
        try { reportError?.Invoke(exception); }
        catch { }
    }
}
