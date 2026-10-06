using System.IO.Pipes;
using AnoCore.Abstractions.Management;
using AnoCore.Runtime.Management;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementPipeBridgeTests
{
    private const string Secret = "pipe-management-secret-that-is-long-enough";
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 21, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void Configuration_RequiresHashedCredentialsOnlyWhenEnabled()
    {
        var disabled = new ManagementBridgeConfiguration();
        var enabled = new ManagementBridgeConfiguration { Enabled = true };

        Assert.AreEqual(0, ManagementBridgeConfiguration.Validate(disabled).Count);
        Assert.IsTrue(
            ManagementBridgeConfiguration.Validate(enabled)
                .Any(error => error.Contains("credential", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CredentialConfiguration_RoundTripsScopesAndHashMaterial()
    {
        var credential = ManagementTokenHasher.Create(
            "panel",
            Secret,
            ManagementScope.ReadStatus | ManagementScope.ManageServer,
            iterations: 100_000);
        var configuration = ManagementCredentialConfiguration.FromCredential(credential);

        var restored = configuration.ToCredential();

        Assert.AreEqual(credential.TokenId, restored.TokenId);
        Assert.AreEqual(credential.Scopes, restored.Scopes);
        Assert.AreEqual(credential.SaltBase64, restored.SaltBase64);
        Assert.AreEqual(credential.HashBase64, restored.HashBase64);
        Assert.IsFalse(
            string.Join("|", configuration.Scopes)
                .Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Configuration_RejectsDuplicateIdsAndUnsupportedScopes()
    {
        var credential = ManagementTokenHasher.Create(
            "panel",
            Secret,
            ManagementScope.ReadStatus,
            iterations: 100_000);
        var first = ManagementCredentialConfiguration.FromCredential(credential);
        var duplicate = ManagementCredentialConfiguration.FromCredential(credential);
        var invalid = ManagementCredentialConfiguration.FromCredential(credential);
        invalid.TokenId = "other";
        invalid.Scopes = ["ReadStatus, ManageServer"];

        var configuration = new ManagementBridgeConfiguration
        {
            Enabled = true,
            Credentials = [first, duplicate, invalid],
        };

        var errors = ManagementBridgeConfiguration.Validate(configuration);

        Assert.IsTrue(errors.Any(error => error.Contains("Duplicate", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("scope", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task PipeClientServer_RoundTripsAuthenticatedHttpRequest()
    {
        var pipeName = $"anocore-test-{Guid.NewGuid():N}";
        using var server = Server(pipeName);
        server.Start();
        var client = new ManagementPipeClient(pipeName, TimeSpan.FromSeconds(2));

        var response = await client.SendAsync(new ManagementHttpRequest(
            "GET",
            "/api/v1/status/health",
            Headers()));

        Assert.AreEqual(200, response.StatusCode);
        Assert.IsTrue(response.Body.Contains("\"ready\":true", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PipeServer_SurvivesInvalidFrameAndServesNextClient()
    {
        var pipeName = $"anocore-test-{Guid.NewGuid():N}";
        var failures = new List<Exception>();
        using var server = Server(pipeName, failures.Add);
        server.Start();

        await using (var raw = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await raw.ConnectAsync(timeout.Token);
            await raw.WriteAsync(new byte[] { 0, 0, 0, 0 }, timeout.Token);
            await raw.FlushAsync(timeout.Token);
        }

        await WaitUntilAsync(() => failures.Count == 1);

        var client = new ManagementPipeClient(pipeName, TimeSpan.FromSeconds(2));
        var response = await client.SendAsync(new ManagementHttpRequest(
            "GET",
            "/api/v1/status/server",
            Headers()));

        Assert.AreEqual(200, response.StatusCode);
        Assert.IsInstanceOfType<InvalidDataException>(failures[0]);
    }

    [TestMethod]
    public async Task PipeServer_DisposeCancelsBlockedRead()
    {
        var pipeName = $"anocore-test-{Guid.NewGuid():N}";
        var server = Server(pipeName);
        server.Start();

        try
        {
            await using var raw = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await raw.ConnectAsync(timeout.Token);

            await Task.Run(server.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            server.Dispose();
        }
    }

    [TestMethod]
    public async Task PipeClient_UsesBoundedConnectTimeout()
    {
        var client = new ManagementPipeClient(
            $"anocore-missing-{Guid.NewGuid():N}",
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.SendAsync(new ManagementHttpRequest(
                "GET",
                "/api/v1/status/health",
                Headers())));
    }

    [TestMethod]
    public async Task PipeClient_TimesOutWhenConnectedPeerNeverResponds()
    {
        var pipeName = $"anocore-silent-{Guid.NewGuid():N}";
        await using var peer = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = peer.WaitForConnectionAsync(deadline.Token);
        var client = new ManagementPipeClient(
            pipeName, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));

        var request = client.SendAsync(new ManagementHttpRequest(
            "GET", "/api/v1/status/health", Headers()), deadline.Token).AsTask();
        await connected;

        await Assert.ThrowsAsync<TimeoutException>(async () => await request);
    }

    [TestMethod]
    public async Task PipeClient_PreservesCallerCancellationAfterConnect()
    {
        var pipeName = $"anocore-cancel-{Guid.NewGuid():N}";
        await using var peer = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource();
        var connected = peer.WaitForConnectionAsync(deadline.Token);
        var client = new ManagementPipeClient(
            pipeName, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        var request = client.SendAsync(new ManagementHttpRequest(
            "GET", "/api/v1/status/health", Headers()), caller.Token).AsTask();
        await connected;
        caller.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(deadline.Token));
    }

    [TestMethod]
    public void PipeClient_RejectsUnboundedExchangeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagementPipeClient("anocore-test", exchangeTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagementPipeClient("anocore-test", exchangeTimeout: TimeSpan.FromMinutes(2)));
    }

    private static ManagementPipeServer Server(
        string pipeName,
        Action<Exception>? onError = null)
    {
        var credential = ManagementTokenHasher.Create(
            "token",
            Secret,
            ManagementScope.ReadStatus,
            iterations: 100_000);
        var gateway = new ManagementApiGateway(
            new ManagementTokenAuthenticator([credential]),
            new ManagementCapabilityRegistry(),
            new StatusProvider());
        return new ManagementPipeServer(
            pipeName,
            new ManagementHttpAdapter(gateway),
            onError);
    }

    private static Dictionary<string, string> Headers()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-AnoCore-Token"] = "token",
            ["X-Correlation-ID"] = "corr-pipe-1",
            ["Authorization"] = $"Bearer {Secret}",
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
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
