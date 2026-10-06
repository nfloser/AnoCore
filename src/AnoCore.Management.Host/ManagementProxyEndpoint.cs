using System.Net;
using System.Text;
using System.Text.Json;
using AnoCore.Abstractions.Management;
using AnoCore.Runtime.Management;
using Microsoft.AspNetCore.Http;

namespace AnoCore.Management.Host;

public sealed class ManagementProxyEndpoint
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly string[] ForwardedHeaders =
    [
        "X-AnoCore-Token",
        "X-Correlation-ID",
        "Authorization",
    ];

    private readonly IManagementPipeTransport _transport;
    private readonly ManagementSidecarOptions _options;

    public ManagementProxyEndpoint(
        IManagementPipeTransport transport,
        ManagementSidecarOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var errors = ManagementSidecarOptions.Validate(options);
        if (errors.Count != 0)
        {
            throw new ArgumentException(
                string.Join(" ", errors),
                nameof(options));
        }
    }

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.IsHttps)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status426UpgradeRequired,
                "https_required").ConfigureAwait(false);
            return;
        }

        if (context.Request.QueryString.HasValue)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "query_not_supported").ConfigureAwait(false);
            return;
        }

        var route = ValidateRoute(context.Request);
        if (!route.Allowed)
        {
            if (route.Allow is not null)
            {
                context.Response.Headers.Allow = route.Allow;
            }

            await WriteErrorAsync(
                context,
                route.StatusCode,
                route.Code).ConfigureAwait(false);
            return;
        }

        if (!TryCopyHeaders(context.Request, out var headers, out var headerStatus))
        {
            await WriteErrorAsync(
                context,
                headerStatus,
                "invalid_headers").ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(
            context.Request,
            context.RequestAborted).ConfigureAwait(false);
        if (body.StatusCode != 0)
        {
            await WriteErrorAsync(
                context,
                body.StatusCode,
                body.Code).ConfigureAwait(false);
            return;
        }

        if (string.Equals(
                context.Request.Method,
                HttpMethods.Get,
                StringComparison.Ordinal)
            && !string.IsNullOrEmpty(body.Value))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "body_not_allowed").ConfigureAwait(false);
            return;
        }

        var forwarded = new ManagementHttpRequest(
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            headers,
            body.Value,
            DirectRemoteIdentity(context.Connection.RemoteIpAddress));

        ManagementHttpResponse response;
        try
        {
            response = await _transport.SendAsync(
                forwarded,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "bridge_unavailable").ConfigureAwait(false);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "bridge_unavailable").ConfigureAwait(false);
            return;
        }
        catch (InvalidDataException)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                "bridge_protocol_error").ConfigureAwait(false);
            return;
        }
        catch (IOException)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "bridge_unavailable").ConfigureAwait(false);
            return;
        }
        catch
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                "bridge_failure").ConfigureAwait(false);
            return;
        }

        await WriteForwardedResponseAsync(
            context,
            response).ConfigureAwait(false);
    }

    private static RouteResult ValidateRoute(HttpRequest request)
    {
        var path = request.Path.Value;
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > ManagementHttpAdapter.MaxPathCharacters
            || path.Any(char.IsControl))
        {
            return RouteResult.NotFound;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !string.Equals(parts[0], "api", StringComparison.Ordinal)
            || !string.Equals(parts[1], ManagementApiVersion.Current, StringComparison.Ordinal))
        {
            return RouteResult.NotFound;
        }

        if (string.Equals(parts[2], "status", StringComparison.Ordinal))
        {
            if (parts[3] is not ("health" or "server" or "players" or "modules"))
            {
                return RouteResult.NotFound;
            }

            return string.Equals(request.Method, HttpMethods.Get, StringComparison.Ordinal)
                ? RouteResult.Success
                : RouteResult.MethodNotAllowed(HttpMethods.Get);
        }

        if (string.Equals(parts[2], "operations", StringComparison.Ordinal)
            && ValidPathSegment(parts[3]))
        {
            return string.Equals(request.Method, HttpMethods.Post, StringComparison.Ordinal)
                ? RouteResult.Success
                : RouteResult.MethodNotAllowed(HttpMethods.Post);
        }

        return RouteResult.NotFound;
    }

    private static bool TryCopyHeaders(
        HttpRequest request,
        out IReadOnlyDictionary<string, string> headers,
        out int statusCode)
    {
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ForwardedHeaders)
        {
            if (!request.Headers.TryGetValue(name, out var values))
            {
                continue;
            }

            if (values.Count != 1)
            {
                headers = copied;
                statusCode = StatusCodes.Status400BadRequest;
                return false;
            }

            var value = values[0];
            if (value is null
                || value.Length > ManagementHttpAdapter.MaxHeaderValueCharacters
                || value.Any(character => character is '\r' or '\n'))
            {
                headers = copied;
                statusCode = value?.Length > ManagementHttpAdapter.MaxHeaderValueCharacters
                    ? StatusCodes.Status431RequestHeaderFieldsTooLarge
                    : StatusCodes.Status400BadRequest;
                return false;
            }

            copied.Add(name, value);
        }

        headers = copied;
        statusCode = 0;
        return true;
    }

    private async Task<BodyResult> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0
            && request.ContentLength > _options.MaximumRequestBodyBytes)
        {
            return BodyResult.Error(
                StatusCodes.Status413PayloadTooLarge,
                "body_too_large");
        }

        using var payload = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (payload.Length + read > _options.MaximumRequestBodyBytes)
            {
                return BodyResult.Error(
                    StatusCodes.Status413PayloadTooLarge,
                    "body_too_large");
            }

            await payload.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken).ConfigureAwait(false);
        }

        if (payload.Length == 0)
        {
            return BodyResult.Success(null);
        }

        try
        {
            return BodyResult.Success(
                StrictUtf8.GetString(payload.GetBuffer(), 0, checked((int)payload.Length)));
        }
        catch (DecoderFallbackException)
        {
            return BodyResult.Error(
                StatusCodes.Status400BadRequest,
                "invalid_utf8");
        }
    }

    private static async Task WriteForwardedResponseAsync(
        HttpContext context,
        ManagementHttpResponse response)
    {
        if (response.StatusCode is < 100 or > 599
            || string.IsNullOrWhiteSpace(response.Body))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                "bridge_protocol_error").ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = response.StatusCode;
        SetSecurityHeaders(context.Response);

        if (response.Headers.TryGetValue("Retry-After", out var retryAfter)
            && ValidResponseHeaderValue(retryAfter))
        {
            context.Response.Headers["Retry-After"] = retryAfter;
        }

        await context.Response.WriteAsync(
            response.Body,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code)
    {
        context.Response.StatusCode = statusCode;
        SetSecurityHeaders(context.Response);
        var payload = JsonSerializer.Serialize(
            new SidecarError(
                ManagementApiVersion.Current,
                CorrelationId(context.Request),
                false,
                code),
            JsonOptions);
        await context.Response.WriteAsync(
            payload,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static void SetSecurityHeaders(HttpResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static string CorrelationId(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Correlation-ID", out var values)
            || values.Count != 1)
        {
            return "invalid";
        }

        var value = values[0]?.Trim();
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 64
            && !value.Any(char.IsControl)
                ? value
                : "invalid";
    }

    private static string? DirectRemoteIdentity(IPAddress? address)
    {
        var value = address?.ToString();
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && !value.Any(char.IsControl)
                ? value
                : null;
    }

    private static bool ValidPathSegment(string value)
        => value.Length is >= 1 and <= 64
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.');

    private static bool ValidResponseHeaderValue(string value)
        => value.Length <= 128
            && !value.Any(character => character is '\r' or '\n');

    private sealed record SidecarError(
        string Version,
        string CorrelationId,
        bool Success,
        string Code);

    private sealed record RouteResult(
        bool Allowed,
        int StatusCode,
        string Code,
        string? Allow)
    {
        public static RouteResult Success { get; } =
            new(true, StatusCodes.Status200OK, string.Empty, null);

        public static RouteResult NotFound { get; } =
            new(false, StatusCodes.Status404NotFound, "not_found", null);

        public static RouteResult MethodNotAllowed(string allow)
            => new(
                false,
                StatusCodes.Status405MethodNotAllowed,
                "method_not_allowed",
                allow);
    }

    private sealed record BodyResult(
        string? Value,
        int StatusCode,
        string Code)
    {
        public static BodyResult Success(string? value)
            => new(value, 0, string.Empty);

        public static BodyResult Error(int statusCode, string code)
            => new(null, statusCode, code);
    }
}
