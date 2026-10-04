using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Management;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementSecurityTests
{
    private const string Secret = "management-test-secret-that-is-long-enough";
    private static readonly ManagementCapabilityId Capability =
        new("server.restart-safe");
    private static readonly ModuleId Owner = new("test.management");
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Gateway_RejectsWrongVersionAndCredentialsBeforeHandler()
    {
        var calls = 0;
        var registry = new ManagementCapabilityRegistry();
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManageServer, ManagementOperationClass.Privileged),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(ManagementOperationResult.Ok("done"));
            });
        var gateway = Gateway(registry, ManagementScope.ManageServer);

        var wrongVersion = await gateway.ExecuteAsync(
            Envelope("v2"),
            Secret);
        var wrongSecret = await gateway.ExecuteAsync(
            Envelope(),
            "different-management-secret-that-is-long-enough");

        Assert.IsFalse(wrongVersion.Success);
        Assert.AreEqual("unsupported_version", wrongVersion.Code);
        Assert.IsFalse(wrongSecret.Success);
        Assert.AreEqual("unauthorized", wrongSecret.Code);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task ScopeDenial_HappensBeforeHandlerAndAudit()
    {
        var audits = new List<ManagementAuditEvent>();
        var calls = 0;
        var registry = Registry(audits);
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManageServer, ManagementOperationClass.Privileged),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(ManagementOperationResult.Ok("done"));
            });
        var gateway = Gateway(registry, ManagementScope.ReadStatus);

        var response = await gateway.ExecuteAsync(Envelope(), Secret);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("forbidden", response.Code);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(0, audits.Count);
    }

    [TestMethod]
    public async Task AuditRequestedFailure_PreventsHandlerExecution()
    {
        var calls = 0;
        var registry = new ManagementCapabilityRegistry(
            new ManagementRateLimiter(new ManagementRateLimitOptions(10, 10, 32)),
            new FixedTime(Now),
            (entry, _) => entry.Phase == "requested"
                ? ValueTask.FromException(new InvalidOperationException("audit down"))
                : ValueTask.CompletedTask);
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManageServer, ManagementOperationClass.Privileged),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(ManagementOperationResult.Ok("done"));
            });
        var gateway = Gateway(registry, ManagementScope.ManageServer);

        var response = await gateway.ExecuteAsync(Envelope(), Secret);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("audit_failed", response.Code);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task CompletionAuditFailure_ReportsAlreadyExecutedOperation()
    {
        var calls = 0;
        var registry = new ManagementCapabilityRegistry(
            new ManagementRateLimiter(new ManagementRateLimitOptions(10, 10, 32)),
            new FixedTime(Now),
            (entry, _) => entry.Phase == "completed"
                ? ValueTask.FromException(new InvalidOperationException("audit down"))
                : ValueTask.CompletedTask);
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManageServer, ManagementOperationClass.Privileged),
            (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(ManagementOperationResult.Ok("executed"));
            });
        var gateway = Gateway(registry, ManagementScope.ManageServer);

        var response = await gateway.ExecuteAsync(Envelope(), Secret);
        var result = response.Payload!;

        Assert.IsFalse(response.Success);
        Assert.AreEqual("audit_failed_after_execution", response.Code);
        Assert.AreEqual("true", result.Data["operation_success"]);
        Assert.AreEqual("ok", result.Data["operation_code"]);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SuccessfulExecution_AuditsRequestedThenCompletedWithoutPayload()
    {
        var audits = new List<ManagementAuditEvent>();
        var registry = Registry(audits);
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManagePlayers, ManagementOperationClass.Privileged),
            (context, request, _) =>
            {
                Assert.AreEqual("corr-1", context.CorrelationId);
                Assert.AreEqual("sensitive-value", request.Arguments["reason"]);
                return ValueTask.FromResult(ManagementOperationResult.Ok("done"));
            });
        var gateway = Gateway(registry, ManagementScope.ManagePlayers);

        var response = await gateway.ExecuteAsync(
            Envelope(arguments: new Dictionary<string, string>
            {
                ["reason"] = "sensitive-value",
            }),
            Secret);

        Assert.IsTrue(response.Success);
        CollectionAssert.AreEqual(
            new[] { "requested", "completed" },
            audits.Select(value => value.Phase).ToArray());
        Assert.IsFalse(string.Join("|", audits).Contains(
            "sensitive-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RateLimiter_IsSeparatedByToken()
    {
        var limiter = new ManagementRateLimiter(
            new ManagementRateLimitOptions(
                ReadRequestsPerMinute: 1,
                PrivilegedRequestsPerMinute: 1,
                MaximumTrackedKeys: 32));
        var registry = new ManagementCapabilityRegistry(limiter, new FixedTime(Now));
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ReadStatus, ManagementOperationClass.Read),
            (_, _, _) => ValueTask.FromResult(ManagementOperationResult.Ok("read")));

        var firstCredential = Credential("one", ManagementScope.ReadStatus);
        var secondCredential = Credential("two", ManagementScope.ReadStatus);
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator([firstCredential, secondCredential]),
            registry,
            new EmptyStatusProvider());

        var first = await gateway.ExecuteAsync(Envelope(tokenId: "one"), Secret);
        var second = await gateway.ExecuteAsync(Envelope(tokenId: "one"), Secret);
        var otherToken = await gateway.ExecuteAsync(Envelope(tokenId: "two"), Secret);

        Assert.IsTrue(first.Success);
        Assert.AreEqual("rate_limited", second.Code);
        Assert.IsTrue(otherToken.Success);
    }

    [TestMethod]
    public async Task HandlerFailure_IsRedactedAndAuditedAsFailure()
    {
        var audits = new List<ManagementAuditEvent>();
        var registry = Registry(audits);
        using var registration = registry.Register(
            Owner,
            Descriptor(ManagementScope.ManageServer, ManagementOperationClass.Privileged),
            (_, _, _) => throw new InvalidOperationException("database-password=secret"));
        var gateway = Gateway(registry, ManagementScope.ManageServer);

        var response = await gateway.ExecuteAsync(Envelope(), Secret);
        var result = response.Payload!;

        Assert.IsFalse(response.Success);
        Assert.AreEqual("handler_failed", response.Code);
        Assert.IsFalse(result.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("failed", audits.Last().Phase);
    }

    [TestMethod]
    public async Task StatusGateway_RequiresAuthenticationAndReadScope()
    {
        var registry = new ManagementCapabilityRegistry();
        var status = new EmptyStatusProvider();
        var noReadGateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [Credential("token", ManagementScope.ManageServer)]),
            registry,
            status);
        var readGateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [Credential("token", ManagementScope.ReadStatus)]),
            registry,
            status);
        var request = new ManagementRequestEnvelope<ManagementStatusRequest>(
            ManagementApiVersion.Current,
            "token",
            "corr-1",
            new ManagementStatusRequest(ManagementStatusResource.Health));

        var forbidden = await noReadGateway.GetStatusAsync(request, Secret);
        var allowed = await readGateway.GetStatusAsync(request, Secret);

        Assert.IsFalse(forbidden.Success);
        Assert.AreEqual("forbidden", forbidden.Code);
        Assert.IsTrue(allowed.Success);
        Assert.IsNotNull(allowed.Payload?.Health);
    }

    [TestMethod]
    public async Task StatusGateway_IsRateLimitedAuditedAndRedactsProviderFailure()
    {
        var audits = new List<ManagementAuditEvent>();
        var limiter = new ManagementRateLimiter(
            new ManagementRateLimitOptions(
                ReadRequestsPerMinute: 1,
                PrivilegedRequestsPerMinute: 10,
                MaximumTrackedKeys: 32));
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [Credential("token", ManagementScope.ReadStatus)]),
            new ManagementCapabilityRegistry(),
            new ThrowingStatusProvider(),
            limiter,
            new FixedTime(Now),
            (entry, _) =>
            {
                audits.Add(entry);
                return ValueTask.CompletedTask;
            });
        var request = new ManagementRequestEnvelope<ManagementStatusRequest>(
            ManagementApiVersion.Current,
            "token",
            "corr-status",
            new ManagementStatusRequest(ManagementStatusResource.Health));

        var failed = await gateway.GetStatusAsync(request, Secret);
        var limited = await gateway.GetStatusAsync(request, Secret);

        Assert.IsFalse(failed.Success);
        Assert.AreEqual("handler_failed", failed.Code);
        CollectionAssert.AreEqual(
            new[] { "requested", "failed" },
            audits.Select(value => value.Phase).ToArray());
        Assert.AreEqual("status.health", audits[0].Capability.Value);
        Assert.AreEqual("handler_failed", audits[1].ResultCode);
        Assert.IsFalse(limited.Success);
        Assert.AreEqual("rate_limited", limited.Code);
    }

    [TestMethod]
    public async Task StatusGateway_RequestedAuditFailurePreventsProviderRead()
    {
        var provider = new CountingStatusProvider();
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator(
                [Credential("token", ManagementScope.ReadStatus)]),
            new ManagementCapabilityRegistry(),
            provider,
            new ManagementRateLimiter(
                new ManagementRateLimitOptions(10, 10, 32)),
            new FixedTime(Now),
            (entry, _) => entry.Phase == "requested"
                ? ValueTask.FromException(new InvalidOperationException("audit down"))
                : ValueTask.CompletedTask);
        var request = new ManagementRequestEnvelope<ManagementStatusRequest>(
            ManagementApiVersion.Current,
            "token",
            "corr-status",
            new ManagementStatusRequest(ManagementStatusResource.Health));

        var response = await gateway.GetStatusAsync(request, Secret);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("audit_failed", response.Code);
        Assert.AreEqual(0, provider.HealthCalls);
    }

    [TestMethod]
    public void Contract_RejectsDefaultCapabilityInvalidScopesAndUnsafeResults()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementOperationRequest(default));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ManagementCapabilityDescriptor(
                Capability,
                "invalid",
                (ManagementScope)(1 << 20),
                ManagementOperationClass.Privileged).Validate());

        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementOperationResult(
                true,
                "ok",
                "unsafe\nmessage"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementOperationResult(
                true,
                "ok",
                "safe",
                new Dictionary<string, string>
                {
                    ["unsafe\nkey"] = "value",
                }));
    }

    [TestMethod]
    public void Registration_IsExclusiveAndDisposable()
    {
        var registry = new ManagementCapabilityRegistry();
        var descriptor = Descriptor(
            ManagementScope.ReadStatus, ManagementOperationClass.Read);
        using var first = registry.Register(
            Owner, descriptor,
            (_, _, _) => ValueTask.FromResult(ManagementOperationResult.Ok("one")));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.Register(
                new ModuleId("test.other"), descriptor,
                (_, _, _) => ValueTask.FromResult(ManagementOperationResult.Ok("two"))));

        first.Dispose();
        using var replacement = registry.Register(
            new ModuleId("test.other"), descriptor,
            (_, _, _) => ValueTask.FromResult(ManagementOperationResult.Ok("two")));
        Assert.AreEqual(1, registry.GetCapabilities().Count);
    }

    [TestMethod]
    public void RequestValidation_RejectsControlsAndOversizedArgumentSets()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementRequestContext(
                new ManagementPrincipal("panel", ManagementScope.ReadStatus),
                "bad\ncorrelation"));

        var arguments = Enumerable.Range(0, 33)
            .ToDictionary(index => $"key{index}", _ => "value");
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementOperationRequest(Capability, arguments));
    }

    private static ManagementCapabilityRegistry Registry(
        ICollection<ManagementAuditEvent> audits)
        => new(
            new ManagementRateLimiter(new ManagementRateLimitOptions(10, 10, 32)),
            new FixedTime(Now),
            (entry, _) =>
            {
                audits.Add(entry);
                return ValueTask.CompletedTask;
            });

    private static ManagementApiGateway Gateway(
        ManagementCapabilityRegistry registry,
        ManagementScope scope)
        => new(
            new ManagementTokenAuthenticator([Credential("token", scope)]),
            registry,
            new EmptyStatusProvider());

    private static ManagementTokenCredential Credential(
        string tokenId,
        ManagementScope scope)
        => ManagementTokenHasher.Create(
            tokenId, Secret, scope, iterations: 100_000);

    private static ManagementRequestEnvelope<ManagementOperationRequest> Envelope(
        string version = ManagementApiVersion.Current,
        string tokenId = "token",
        IReadOnlyDictionary<string, string>? arguments = null)
        => new(
            version,
            tokenId,
            "corr-1",
            new ManagementOperationRequest(Capability, arguments));

    private static ManagementCapabilityDescriptor Descriptor(
        ManagementScope scope,
        ManagementOperationClass operationClass)
        => new(Capability, "Safe test capability.", scope, operationClass);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingStatusProvider : IManagementStatusProvider
    {
        public ValueTask<ManagementHealthSnapshot> GetHealthAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<ManagementHealthSnapshot>(
                new InvalidOperationException("database-password=secret"));

        public ValueTask<ManagementServerStatus> GetServerAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CountingStatusProvider : IManagementStatusProvider
    {
        public int HealthCalls { get; private set; }

        public ValueTask<ManagementHealthSnapshot> GetHealthAsync(
            CancellationToken cancellationToken = default)
        {
            HealthCalls++;
            return ValueTask.FromResult(new ManagementHealthSnapshot(true, "ready", Now));
        }

        public ValueTask<ManagementServerStatus> GetServerAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class EmptyStatusProvider : IManagementStatusProvider
    {
        public ValueTask<ManagementHealthSnapshot> GetHealthAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ManagementHealthSnapshot(true, "ready", Now));

        public ValueTask<ManagementServerStatus> GetServerAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ManagementServerStatus(
                ManagementApiVersion.Current, 1, 0, Now));

        public ValueTask<IReadOnlyList<ManagementPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ManagementPlayerStatus>>([]);

        public ValueTask<IReadOnlyList<ManagementModuleStatus>> GetModulesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ManagementModuleStatus>>([]);
    }
}
