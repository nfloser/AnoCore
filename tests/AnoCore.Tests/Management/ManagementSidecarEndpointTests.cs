using System.Net;
using System.Text;
using AnoCore.Management.Host;
using AnoCore.Runtime.Management;
using Microsoft.AspNetCore.Http;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementSidecarEndpointTests
{
    private const string Secret = "sidecar-test-secret-that-is-long-enough";

    [TestMethod]
    public void Options_RejectUnsafeBounds()
    {
        var options = new ManagementSidecarOptions
        {
            PipeName = "bad/name",
            ConnectTimeoutMilliseconds = 99,
            MaximumRequestBodyBytes =
                ManagementSidecarOptions.HardMaximumRequestBodyBytes + 1,
            RequestsPerMinutePerClient = 0,
        };

        var errors = ManagementSidecarOptions.Validate(options);

        Assert.AreEqual(4, errors.Count);
    }

    [TestMethod]
    public async Task Endpoint_RejectsInsecureHttpBeforePipeAccess()
    {
        var transport = new FakeTransport();
        var endpoint = Endpoint(transport);
        var context = Context(
            HttpMethods.Get,
            "/api/v1/status/health",
            https: false);
        AddCredentials(context);

        await endpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
        Assert.AreEqual(0, transport.Calls);
        StringAssert.Contains(await ResponseBody(context), "https_required");
    }

    [TestMethod]
    public async Task Endpoint_ForwardsOnlyBoundedManagementHeadersAndDirectPeer()
    {
        var transport = new FakeTransport
        {
            Response = Response(StatusCodes.Status200OK),
        };
        var endpoint = Endpoint(transport);
        var context = Context(HttpMethods.Get, "/api/v1/status/server");
        AddCredentials(context);
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        context.Request.Headers.Cookie = "should-not-cross-the-pipe";

        await endpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual(1, transport.Calls);
        Assert.IsNotNull(transport.LastRequest);
        Assert.AreEqual(3, transport.LastRequest.Headers!.Count);
        Assert.IsFalse(transport.LastRequest.Headers.ContainsKey("X-Forwarded-For"));
        Assert.IsFalse(transport.LastRequest.Headers.ContainsKey("Cookie"));
        Assert.AreEqual("203.0.113.7", transport.LastRequest.RemoteIdentity);
        Assert.AreEqual("/api/v1/status/server", transport.LastRequest.Path);
    }

    [TestMethod]
    public async Task Endpoint_RejectsQueryAndWrongMethodBeforePipeAccess()
    {
        var transport = new FakeTransport();
        var endpoint = Endpoint(transport);

        var query = Context(HttpMethods.Get, "/api/v1/status/health");
        AddCredentials(query);
        query.Request.QueryString = new QueryString("?secret=value");
        await endpoint.HandleAsync(query);

        var method = Context(HttpMethods.Delete, "/api/v1/status/health");
        AddCredentials(method);
        await endpoint.HandleAsync(method);

        Assert.AreEqual(StatusCodes.Status400BadRequest, query.Response.StatusCode);
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, method.Response.StatusCode);
        Assert.AreEqual(HttpMethods.Get, method.Response.Headers.Allow.ToString());
        Assert.AreEqual(0, transport.Calls);
    }

    [TestMethod]
    public async Task Endpoint_RejectsOversizedBodyBeforePipeAccess()
    {
        var transport = new FakeTransport();
        var options = new ManagementSidecarOptions
        {
            MaximumRequestBodyBytes = 32,
        };
        var endpoint = new ManagementProxyEndpoint(transport, options);
        var context = Context(
            HttpMethods.Post,
            "/api/v1/operations/server.restart");
        AddCredentials(context);
        context.Request.ContentLength = 33;
        context.Request.Body = new MemoryStream(new byte[33]);

        await endpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.AreEqual(0, transport.Calls);
    }

    [TestMethod]
    public async Task Endpoint_ReturnsUnavailableWithoutEchoingBearerSecret()
    {
        var transport = new FakeTransport
        {
            Error = new TimeoutException(),
        };
        var endpoint = Endpoint(transport);
        var context = Context(HttpMethods.Get, "/api/v1/status/health");
        AddCredentials(context);

        await endpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        var body = await ResponseBody(context);
        StringAssert.Contains(body, "bridge_unavailable");
        Assert.IsFalse(body.Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Endpoint_PropagatesStatusBodyAndOnlyExplicitSafeHeaders()
    {
        var transport = new FakeTransport
        {
            Response = new ManagementHttpResponse(
                StatusCodes.Status429TooManyRequests,
                """{"version":"v1","success":false,"code":"rate_limited"}""",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = "application/json",
                    ["Cache-Control"] = "private",
                    ["X-Content-Type-Options"] = "unsafe",
                    ["Retry-After"] = "12",
                    ["Set-Cookie"] = "session=should-not-escape",
                    ["Server"] = "hidden",
                }),
        };
        var endpoint = Endpoint(transport);
        var context = Context(HttpMethods.Get, "/api/v1/status/health");
        AddCredentials(context);

        await endpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        StringAssert.Contains(await ResponseBody(context), "rate_limited");
        Assert.AreEqual("12", context.Response.Headers["Retry-After"].ToString());
        Assert.AreEqual("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.AreEqual("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.IsFalse(context.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Server"));
    }

    private static ManagementProxyEndpoint Endpoint(FakeTransport transport)
        => new(transport, new ManagementSidecarOptions());

    private static DefaultHttpContext Context(
        string method,
        string path,
        bool https = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return context;
    }

    private static void AddCredentials(DefaultHttpContext context)
    {
        context.Request.Headers["X-AnoCore-Token"] = "panel";
        context.Request.Headers["X-Correlation-ID"] = "corr-sidecar-1";
        context.Request.Headers.Authorization = $"Bearer {Secret}";
    }

    private static ManagementHttpResponse Response(int status)
        => new(
            status,
            """{"version":"v1","success":true,"code":"ok"}""",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json; charset=utf-8",
                ["Cache-Control"] = "no-store",
                ["X-Content-Type-Options"] = "nosniff",
            });

    private static async Task<string> ResponseBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeTransport : IManagementPipeTransport
    {
        public int Calls { get; private set; }

        public ManagementHttpRequest? LastRequest { get; private set; }

        public ManagementHttpResponse Response { get; set; } =
            ManagementSidecarEndpointTests.Response(StatusCodes.Status200OK);

        public Exception? Error { get; set; }

        public ValueTask<ManagementHttpResponse> SendAsync(
            ManagementHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            if (Error is not null)
            {
                throw Error;
            }

            return ValueTask.FromResult(Response);
        }
    }
}
