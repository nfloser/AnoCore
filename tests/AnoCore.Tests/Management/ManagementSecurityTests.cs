using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;
using AnoCore.Runtime.Management;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementSecurityTests
{
    private static readonly ManagementCapabilityId Capability =
        new("server.restart-safe");
    private static readonly ModuleId Owner = new("test.management");
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);

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

        var result = await registry.ExecuteAsync(
            Context(ManagementScope.ReadStatus),
            new ManagementOperationRequest(Capability));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("forbidden", result.Code);
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

        var result = await registry.ExecuteAsync(
            Context(ManagementScope.ManageServer),
            new ManagementOperationRequest(Capability));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("audit_failed", result.Code);
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

        var result = await registry.ExecuteAsync(
            Context(ManagementScope.ManageServer),
            new ManagementOperationRequest(Capability));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("audit_failed_after_execution", result.Code);
        Assert.AreEqual("true", result.Data!["operation_success"]);
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

        var result = await registry.ExecuteAsync(
            Context(ManagementScope.ManagePlayers),
            new ManagementOperationRequest(
                Capability,
                new Dictionary<string, string>
                {
                    ["reason"] = "sensitive-value",
                }));

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "requested", "completed" },
            audits.Select(value => value.Phase).ToArray());
        Assert.IsFalse(string.Join("|", audits).Contains(
            "sensitive-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RateLimiter_IsSeparatedByOperationClassAndToken()
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

        var first = await registry.ExecuteAsync(
            Context(ManagementScope.ReadStatus, "one"),
            new ManagementOperationRequest(Capability));
        var second = await registry.ExecuteAsync(
            Context(ManagementScope.ReadStatus, "one"),
            new ManagementOperationRequest(Capability));
        var otherToken = await registry.ExecuteAsync(
            Context(ManagementScope.ReadStatus, "two"),
            new ManagementOperationRequest(Capability));

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

        var result = await registry.ExecuteAsync(
            Context(ManagementScope.ManageServer),
            new ManagementOperationRequest(Capability));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("handler_failed", result.Code);
        Assert.IsFalse(result.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("failed", audits.Last().Phase);
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

    private static ManagementCapabilityDescriptor Descriptor(
        ManagementScope scope,
        ManagementOperationClass operationClass)
        => new(Capability, "Safe test capability.", scope, operationClass);

    private static ManagementRequestContext Context(
        ManagementScope scope,
        string token = "token")
        => new(
            new ManagementPrincipal(token, scope),
            "corr-1",
            "test-remote");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
