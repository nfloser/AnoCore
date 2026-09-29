using AnoCore.Abstractions.Configuration;
using AnoCore.Runtime.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Configuration;

[TestClass]
public sealed class JsonConfigStoreTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "anocore-config-tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingConfigCreatesValidatedDefaults()
    {
        var store = new JsonConfigStore(_root);

        var config = await store.LoadAsync(
            "core",
            () => new TestConfig("default", 2),
            value => value.Count > 0 ? [] : ["Count must be positive."]);

        Assert.AreEqual(new TestConfig("default", 2), config);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "core.json")));
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsTypedConfig()
    {
        var store = new JsonConfigStore(_root);
        var expected = new TestConfig("saved", 7);

        await store.SaveAsync("module.settings", expected);
        var actual = await store.LoadAsync("module.settings", () => new TestConfig("fallback", 1));

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJsonThrowsExplicitException()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.json"), "{ not json }");
        var store = new JsonConfigStore(_root);

        var exception = await Assert.ThrowsExactlyAsync<ConfigStoreException>(async () =>
            await store.LoadAsync("broken", () => new TestConfig("fallback", 1)));

        StringAssert.Contains(exception.Message, "broken");
    }

    [TestMethod]
    public async Task LoadAsync_ValidationFailureDoesNotWriteInvalidDefaults()
    {
        var store = new JsonConfigStore(_root);

        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await store.LoadAsync(
                "invalid",
                () => new TestConfig("bad", 0),
                _ => ["Count must be positive."]));

        Assert.IsFalse(File.Exists(Path.Combine(_root, "invalid.json")));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsTraversalNames()
    {
        var store = new JsonConfigStore(_root);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await store.SaveAsync("../secrets", new TestConfig("nope", 1)));
    }

    [TestMethod]
    public async Task LoadVersionedAsync_MigratesLegacyConfigAndRewritesEnvelope()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "versioned.json");
        await File.WriteAllTextAsync(path, """{"Name":"legacy","Count":2}""");
        var store = new JsonConfigStore(_root);

        var config = await store.LoadVersionedAsync(
            "versioned", 2, () => new TestConfig("default", 1),
            [
                new ConfigMigration<TestConfig>(0, value => value with { Count = value.Count + 1 }),
                new ConfigMigration<TestConfig>(1, value => value with { Name = value.Name + "-v2" }),
            ]);

        Assert.AreEqual(new TestConfig("legacy-v2", 3), config);
        var rewritten = await File.ReadAllTextAsync(path);
        StringAssert.Contains(rewritten, "\"$schemaVersion\": 2");
        StringAssert.Contains(rewritten, "\"value\"");
    }

    [TestMethod]
    public async Task LoadVersionedAsync_MigrationGapPreservesSource()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "gap.json");
        const string source = """{"Name":"legacy","Count":2}""";
        await File.WriteAllTextAsync(path, source);
        var store = new JsonConfigStore(_root);

        var exception = await Assert.ThrowsExactlyAsync<ConfigMigrationException>(async () =>
            await store.LoadVersionedAsync(
                "gap", 2, () => new TestConfig("default", 1),
                [new ConfigMigration<TestConfig>(0, value => value)]));

        Assert.AreEqual(1, exception.FromVersion);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task LoadVersionedAsync_RejectsFutureVersionWithoutOverwrite()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "future.json");
        const string source = """
            {
              "$schemaVersion": 3,
              "value": { "Name": "future", "Count": 2 }
            }
            """;
        await File.WriteAllTextAsync(path, source);
        var store = new JsonConfigStore(_root);

        await Assert.ThrowsExactlyAsync<ConfigStoreException>(async () =>
            await store.LoadVersionedAsync(
                "future", 2, () => new TestConfig("default", 1),
                [
                    new ConfigMigration<TestConfig>(0, value => value),
                    new ConfigMigration<TestConfig>(1, value => value),
                ]));

        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task LoadVersionedAsync_InvalidMigratedValuePreservesLegacySource()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "invalid-migration.json");
        const string source = """{"Name":"legacy","Count":2}""";
        await File.WriteAllTextAsync(path, source);
        var store = new JsonConfigStore(_root);

        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await store.LoadVersionedAsync(
                "invalid-migration", 1, () => new TestConfig("default", 1),
                [new ConfigMigration<TestConfig>(0, value => value with { Count = 0 })],
                value => value.Count > 0 ? [] : ["Count must be positive."]));

        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    private sealed record TestConfig(string Name, int Count);
}
