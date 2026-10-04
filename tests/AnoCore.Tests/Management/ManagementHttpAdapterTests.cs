using System.Text.Json;
using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;
using AnoCore.Runtime.Management;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementHttpAdapterTests
{
    private const string Secret = "management-http-secret-that-is-long-enough";
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 21, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task StatusRoute_ReturnsVersionedJsonWithoutCaching()
    {
        var adapter = Adapter(ManagementScope.ReadStatus);

        var response = await adapter.HandleAsync(Request(
            "GET",
            "/api/v1/status/health"));

        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers["Cache-Control"]);
        using var json = JsonDocument.Parse(response.Body);
        Assert.AreEqual("v1", json.RootElement.GetProperty("version").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("success").GetBoolean());
        Assert.IsTrue(
            json.RootElement.GetProperty("payload")
                .GetProperty("health")
                .GetProperty("ready")
                .GetBoolean());
    }

    [TestMethod]
    public async Task OperationRoute_ExecutesRegisteredCapabilityWithArguments()
    {
        IReadOnlyDictionary<string, string>? observed = null;
        var registry = new ManagementCapabilityRegistry();
        using var registration = registry.Register(
            new ModuleId("test.http"),
            new ManagementCapabilityDescriptor(
                new ManagementCapabilityId("server.safe-reload"),
                "Safe reload.",
                ManagementScope.ManageServer,
                ManagementOperationClass.Privileged),
            (_, request, _) =>
            {
                observed = request.Arguments;
                return ValueTask.FromResult(
                    ManagementOperationResult.Ok(
                        "reloaded",
                        new Dictionary<string, string> { ["state"] = "ready" }));
            });
        var adapter = Adapter(ManagementScope.ManageServer, registry);

        var response = await adapter.HandleAsync(Request(
            "POST",
            "/api/v1/operations/server.safe-reload",
            """{"module":"ranks","reason":"operator request"}"""));

        Assert.AreEqual(200, response.StatusCode);
        Assert.IsNotNull(observed);
        Assert.AreEqual("ranks", observed["module"]);
        Assert.AreEqual("operator request", observed["reason"]);
        Assert.IsFalse(response.Body.Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WrongSecret_ReturnsUnauthorizedWithoutEchoingCredential()
    {
        var adapter = Adapter(ManagementScope.ReadStatus);
        var request = Request("GET", "/api/v1/status/server");
        request = request with
        {
            Headers = Headers("different-management-secret-that-is-long-enough"),
        };

        var response = await adapter.HandleAsync(request);

        Assert.AreEqual(401, response.StatusCode);
        Assert.IsFalse(
            response.Body.Contains(
                "different-management-secret-that-is-long-enough",
                StringComparison.Ordinal));
        Assert.IsTrue(response.Headers.ContainsKey("X-Content-Type-Options"));
    }

    [TestMethod]
    public async Task MissingCorrelationHeader_IsRejectedBeforeGateway()
    {
        var adapter = Adapter(ManagementScope.ReadStatus);
        var headers = Headers(Secret);
        headers.Remove("X-Correlation-ID");

        var response = await adapter.HandleAsync(
            new ManagementHttpRequest(
                "GET",
                "/api/v1/status/health",
                headers));

        Assert.AreEqual(400, response.StatusCode);
        Assert.IsTrue(response.Body.Contains("invalid_request", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ScopeDenial_MapsToForbidden()
    {
        var registry = new ManagementCapabilityRegistry();
        using var registration = registry.Register(
            new ModuleId("test.http"),
            new ManagementCapabilityDescriptor(
                new ManagementCapabilityId("server.safe-reload"),
                "Safe reload.",
                ManagementScope.ManageServer,
                ManagementOperationClass.Privileged),
            (_, _, _) => ValueTask.FromResult(ManagementOperationResult.Ok("done")));
        var adapter = Adapter(ManagementScope.ReadStatus, registry);

        var response = await adapter.HandleAsync(Request(
            "POST",
            "/api/v1/operations/server.safe-reload",
            "{}"));

        Assert.AreEqual(403, response.StatusCode);
    }

    [TestMethod]
    public async Task MalformedAndOversizedBodies_AreRejectedBeforeExecution()
    {
        var calls = 0;
        var registry = new ManagementCapabilityRegistry();
        using var registration = registry.Register(
            new ModuleId("test.http"),
            new ManagementCapabilityDescriptor(
                new ManagementCapabilityId("server.safe-reload"),
                "Safe reload.",
                ManagementScope.ManageServer,
                ManagementOperationClass.Privileged),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(ManagementOperationResult.Ok("done"));
            });
        var adapter = Adapter(ManagementScope.ManageServer, registry);

        var malformed = await adapter.HandleAsync(Request(
            "POST",
            "/api/v1/operations/server.safe-reload",
            "{"));
        var oversized = await adapter.HandleAsync(Request(
            "POST",
            "/api/v1/operations/server.safe-reload",
            new string('x', ManagementHttpAdapter.MaxBodyCharacters + 1)));

        Assert.AreEqual(400, malformed.StatusCode);
        Assert.AreEqual(413, oversized.StatusCode);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task UnsupportedVersionAndUnknownRoute_AreDeterministic()
    {
        var adapter = Adapter(ManagementScope.ReadStatus);

        var version = await adapter.HandleAsync(
            Request("GET", "/api/v2/status/health"));
        var route = await adapter.HandleAsync(
            Request("GET", "/api/v1/unknown/value"));

        Assert.AreEqual(400, version.StatusCode);
        Assert.IsTrue(version.Body.Contains("unsupported_version", StringComparison.Ordinal));
        Assert.AreEqual(404, route.StatusCode);
        Assert.IsTrue(route.Body.Contains("not_found", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StatusRateLimit_MapsToTooManyRequests()
    {
        var limiter = new ManagementRateLimiter(
            new ManagementRateLimitOptions(
                ReadRequestsPerMinute: 1,
                PrivilegedRequestsPerMinute: 10,
                MaximumTrackedKeys: 32));
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [ManagementTokenHasher.Create(
                    "token", Secret, ManagementScope.ReadStatus, iterations: 100_000)]),
            new ManagementCapabilityRegistry(),
            new StatusProvider(),
            limiter,
            new FixedTime(Now));
        var adapter = new ManagementHttpAdapter(gateway);

        var first = await adapter.HandleAsync(Request(
            "GET", "/api/v1/status/health"));
        var limited = await adapter.HandleAsync(Request(
            "GET", "/api/v1/status/health"));

        Assert.AreEqual(200, first.StatusCode);
        Assert.AreEqual(429, limited.StatusCode);
        Assert.IsTrue(limited.Body.Contains("rate_limited", StringComparison.Ordinal));
    }

    private static ManagementHttpAdapter Adapter(
        ManagementScope scopes,
        ManagementCapabilityRegistry? registry = null)
    {
        registry ??= new ManagementCapabilityRegistry();
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [ManagementTokenHasher.Create(
                    "token", Secret, scopes, iterations: 100_000)]),
            registry,
            new StatusProvider());
        return new ManagementHttpAdapter(gateway);
    }

    private static ManagementHttpRequest Request(
        string method,
        string path,
        string? body = null)
        => new(
            method,
            path,
            Headers(Secret),
            body,
            "test-client");

    private static Dictionary<string, string> Headers(string secret)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-AnoCore-Token"] = "token",
            ["X-Correlation-ID"] = "corr-http-1",
            ["Authorization"] = $"Bearer {secret}",
        };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StatusProvider : IManagementStatusProvider
    {
        public ValueTask<ManagementHealthSnapshot> GetHealthAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                new ManagementHealthSnapshot(true, "ready", Now));

        public ValueTask<ManagementServerStatus> GetServerAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                new ManagementServerStatus("v1", 1, 0, Now));

        public ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ManagementPlayerStatus>>([]);

        public ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ManagementModuleStatus>>([]);
    }
}
