using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Modules;

namespace AnoCore.Tests.Runtime;

[TestClass]
public sealed class ExternalModuleCatalogTests
{
    [TestMethod]
    public async Task LoadsSdkOnlyAssemblyAndOwnsItsCommandThroughHostShutdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anocore-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filename = Path.GetFileName(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"), Path.Combine(directory, filename));
            var commands = new CommandRegistry(new Permissions());
            var host = new ModuleHost(new Context(commands));
            var catalog = new ExternalModuleCatalog(host);
            var results = await catalog.LoadAsync(directory, new ExternalModuleConfiguration { Assemblies = [filename] });
            Assert.HasCount(1, results);
            Assert.IsTrue(results[0].Loaded);
            Assert.IsTrue((await commands.ExecuteAsync("!anofixture", null)).Success);
            await host.ShutdownAsync();
            Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anofixture", null)).FailureReason);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task InvalidAssemblyIsIsolatedAndUnconfiguredFilesAreNotScanned()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anocore-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "bad.dll"), [1, 2, 3]);
            var filename = Path.GetFileName(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"), Path.Combine(directory, filename));
            var host = new ModuleHost(new Context(new CommandRegistry(new Permissions())));
            var results = await new ExternalModuleCatalog(host).LoadAsync(directory,
                new ExternalModuleConfiguration { Assemblies = ["bad.dll", filename] });
            Assert.HasCount(2, results);
            Assert.HasCount(1, results.Where(item => item.Loaded).ToArray());
            await host.ShutdownAsync();
            var emptyHost = new ModuleHost(new Context(new CommandRegistry(new Permissions())));
            Assert.IsEmpty(await new ExternalModuleCatalog(emptyHost).LoadAsync(directory, new ExternalModuleConfiguration()));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("../escape.dll")]
    [DataRow("/tmp/escape.dll")]
    [DataRow("folder/module.dll")]
    [DataRow("module.txt")]
    public void ConfigurationRejectsPathsAndNonAssemblies(string filename)
        => Assert.IsNotEmpty(ExternalModuleConfiguration.Validate(new() { Assemblies = [filename] }));

    [TestMethod]
    public void ConfigurationRejectsDuplicatesAndExcessiveAssemblies()
    {
        Assert.IsNotEmpty(ExternalModuleConfiguration.Validate(new() { Assemblies = ["one.dll", "ONE.dll"] }));
        Assert.IsNotEmpty(ExternalModuleConfiguration.Validate(new() { Assemblies = Enumerable.Range(0, 33).Select(i => $"module{i}.dll").ToList() }));
    }

    [TestMethod]
    public async Task DuplicateModuleIdentityKeepsFirstOwnerAndRejectsImplementationDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anocore-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"), Path.Combine(directory, "one.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"), Path.Combine(directory, "two.dll"));
            File.Copy(typeof(ExternalModuleCatalogTests).Assembly.Location, Path.Combine(directory, "implementation.dll"));
            var commands = new CommandRegistry(new Permissions());
            var host = new ModuleHost(new Context(commands));
            var results = await new ExternalModuleCatalog(host).LoadAsync(directory,
                new ExternalModuleConfiguration { Assemblies = ["one.dll", "two.dll", "implementation.dll"] });
            Assert.HasCount(1, results.Where(item => item.Loaded).ToArray());
            Assert.HasCount(2, results.Where(item => !item.Loaded).ToArray());
            Assert.IsTrue((await commands.ExecuteAsync("!anofixture", null)).Success);
            await host.ShutdownAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LinkedAssembliesAndDirectoriesAreRejected(bool linkDirectory)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Symbolic link setup requires an enabled Windows developer policy.");
        var directory = Path.Combine(Path.GetTempPath(), "anocore-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "target");
            var modules = Path.Combine(directory, "modules");
            Directory.CreateDirectory(target);
            var filename = Path.GetFileName(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "AnoCore.TestModuleFixture.dll"), Path.Combine(target, filename));
            if (linkDirectory) Directory.CreateSymbolicLink(modules, target);
            else
            {
                Directory.CreateDirectory(modules);
                File.CreateSymbolicLink(Path.Combine(modules, filename), Path.Combine(target, filename));
            }
            var host = new ModuleHost(new Context(new CommandRegistry(new Permissions())));
            var results = await new ExternalModuleCatalog(host).LoadAsync(modules,
                new ExternalModuleConfiguration { Assemblies = [filename] });
            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].Loaded);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Context(IAnoCommandRegistry commands) : IAnoModuleContext, IServiceProvider
    {
        public IServiceProvider Services => this;
        public object? GetService(Type type) => type == typeof(IAnoCommandRegistry) ? commands : null;
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
