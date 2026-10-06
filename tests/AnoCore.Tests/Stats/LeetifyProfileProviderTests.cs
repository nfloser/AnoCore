using System.Net;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class LeetifyProfileProviderTests
{
    private static readonly PlayerId Player = new(76561198096123254);

    [TestMethod]
    public async Task ReadAsync_UsesSteam64AndBearerKeyAndPreservesSelectedMetrics()
    {
        HttpRequestMessage? captured = null;
        var handler = new Handler(async (request, _) =>
        {
            captured = Clone(request);
            await Task.Yield();
            return Json("""
                {
                  "privacy_mode":"public",
                  "steam64_id":"76561198096123254",
                  "name":"Ativ",
                  "rating":{
                    "aim":95.25,
                    "positioning":61,
                    "utility":42.500,
                    "clutch":7,
                    "opening":8
                  }
                }
                """);
        });
        using var client = new HttpClient(handler);
        var provider = new LeetifyHttpProfileProvider(
            client, "top-secret-key", timeout: TimeSpan.FromSeconds(1));

        var result = await provider.ReadAsync(Player);

        Assert.AreEqual(LeetifyLookupStatus.Available, result.Status);
        Assert.IsNotNull(result.Profile);
        Assert.AreEqual(Player, result.Profile.Player);
        CollectionAssert.AreEqual(
            new[] { "Aim=95.25", "Positioning=61", "Utility=42.500" },
            result.Profile.Metrics.Select(metric => $"{metric.Name}={metric.Value}").ToArray());
        Assert.AreEqual(
            $"https://leetify.com/app/profile/{Player.SteamId64}",
            result.Profile.ProfileUri.AbsoluteUri.TrimEnd('/'));
        Assert.IsNotNull(captured);
        Assert.AreEqual(
            $"/v3/profile?steam64_id={Player.SteamId64}",
            captured.RequestUri!.PathAndQuery);
        Assert.AreEqual("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.AreEqual("top-secret-key", captured.Headers.Authorization?.Parameter);
    }

    [TestMethod]
    public async Task ReadAsync_MapsProviderAvailabilityWithoutLeakingSecret()
    {
        foreach (var sample in new[]
        {
            (HttpStatusCode.NotFound, LeetifyLookupStatus.NotFound),
            (HttpStatusCode.TooManyRequests, LeetifyLookupStatus.RateLimited),
            (HttpStatusCode.Unauthorized, LeetifyLookupStatus.Unauthorized),
            (HttpStatusCode.Forbidden, LeetifyLookupStatus.Unauthorized),
            (HttpStatusCode.InternalServerError, LeetifyLookupStatus.Unavailable),
        })
        {
            using var client = new HttpClient(new Handler(
                (_, _) => Task.FromResult(new HttpResponseMessage(sample.Item1))));
            var provider = new LeetifyHttpProfileProvider(client, "never-print-me");

            var result = await provider.ReadAsync(Player);

            Assert.AreEqual(sample.Item2, result.Status);
            Assert.IsFalse(result.ToString()!.Contains("never-print-me", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Constructor_RejectsInvalidAuthorizationValue()
    {
        using var client = new HttpClient(new Handler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        foreach (var key in new[] { "bad key with spaces", "key\tvalue", "key\r\nvalue", "key\u007f", "keyé" })
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                new LeetifyHttpProfileProvider(client, key));
        }
    }

    [TestMethod]
    public async Task ReadAsync_PrivateProfileIsNotPresented()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""
            {
              "privacy_mode":"private",
              "steam64_id":"76561198096123254",
              "name":"Hidden",
              "rating":{"aim":1,"positioning":2,"utility":3}
            }
            """))));
        var provider = new LeetifyHttpProfileProvider(client, "key");

        var result = await provider.ReadAsync(Player);

        Assert.AreEqual(LeetifyLookupStatus.Private, result.Status);
        Assert.IsNull(result.Profile);
    }

    [TestMethod]
    public async Task ReadAsync_RejectsWrongIdentityMalformedAndOversizedPayloads()
    {
        foreach (var body in new[]
        {
            """{"privacy_mode":"public","steam64_id":"76561198000000000","name":"Other","rating":{"aim":1,"positioning":2,"utility":3}}""",
            """{"privacy_mode":"public","steam64_id":"76561198096123254","name":"Missing rating"}""",
            """not-json""",
        })
        {
            using var client = new HttpClient(new Handler(
                (_, _) => Task.FromResult(Json(body))));
            var provider = new LeetifyHttpProfileProvider(client, "key");

            var result = await provider.ReadAsync(Player);

            Assert.AreEqual(LeetifyLookupStatus.InvalidResponse, result.Status);
        }

        using var oversizedClient = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(new string('x', 2049)))));
        var bounded = new LeetifyHttpProfileProvider(
            oversizedClient, "key", maxResponseBytes: 2048);
        Assert.AreEqual(
            LeetifyLookupStatus.InvalidResponse,
            (await bounded.ReadAsync(Player)).Status);
    }

    [TestMethod]
    public async Task ReadAsync_TimeoutAndCallerCancellationAreDistinct()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Json("{}");
        }));
        var provider = new LeetifyHttpProfileProvider(
            client, "key", timeout: TimeSpan.FromMilliseconds(25));

        var timedOut = await provider.ReadAsync(Player);
        Assert.AreEqual(LeetifyLookupStatus.Timeout, timedOut.Status);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await provider.ReadAsync(Player, cancelled.Token));
    }

    [TestMethod]
    public async Task ReadAsync_RespectsConfiguredConcurrencyBound()
    {
        var active = 0;
        var maximum = 0;
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            var now = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, now);
            entered.TrySetResult(true);
            try
            {
                await release.Task.WaitAsync(token);
                return Json("""
                    {"privacy_mode":"public","steam64_id":"76561198096123254","name":"A",
                     "rating":{"aim":1,"positioning":2,"utility":3}}
                    """);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }));
        var provider = new LeetifyHttpProfileProvider(
            client, "key", timeout: TimeSpan.FromSeconds(2), maxConcurrency: 1);

        var first = provider.ReadAsync(Player).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = await provider.ReadAsync(Player);
        Assert.AreEqual(LeetifyLookupStatus.RateLimited, second.Status);
        Assert.AreEqual(1, maximum);
        release.TrySetResult(true);

        Assert.AreEqual(
            LeetifyLookupStatus.Available,
            (await first).Status);
        Assert.AreEqual(1, maximum);
    }

    [TestMethod]
    public async Task ReadAsync_EnforcesLocalPerMinuteRequestBudget()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""
            {"privacy_mode":"public","steam64_id":"76561198096123254","name":"A",
             "rating":{"aim":1,"positioning":2,"utility":3}}
            """))));
        var provider = new LeetifyHttpProfileProvider(
            client,
            "key",
            maxRequestsPerMinute: 1);

        Assert.AreEqual(
            LeetifyLookupStatus.Available,
            (await provider.ReadAsync(Player)).Status);
        Assert.AreEqual(
            LeetifyLookupStatus.RateLimited,
            (await provider.ReadAsync(Player)).Status);
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
        };

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        clone.Headers.Authorization = request.Headers.Authorization;
        return clone;
    }

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
