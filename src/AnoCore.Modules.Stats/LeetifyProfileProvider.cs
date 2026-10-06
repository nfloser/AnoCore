using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Stats;

public enum LeetifyLookupStatus
{
    Available,
    NotFound,
    Private,
    RateLimited,
    Unauthorized,
    Timeout,
    Unavailable,
    InvalidResponse,
}

public sealed record LeetifyMetric(string Name, string Value);

public sealed record LeetifyProfileContext(
    PlayerId Player,
    string Name,
    IReadOnlyList<LeetifyMetric> Metrics,
    Uri ProfileUri);

public sealed record LeetifyLookupResult(
    LeetifyLookupStatus Status,
    LeetifyProfileContext? Profile = null);

public interface ILeetifyProfileProvider
{
    ValueTask<LeetifyLookupResult> ReadAsync(
        PlayerId player,
        CancellationToken cancellationToken = default);
}

public sealed class LeetifyHttpProfileProvider : ILeetifyProfileProvider
{
    public const int DefaultMaxResponseBytes = 128 * 1024;
    public const int DefaultMaxConcurrency = 2;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private static readonly Uri DefaultBaseUri =
        new("https://api-public.cs-prod.leetify.com/");
    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly TimeSpan _timeout;
    private readonly int _maxResponseBytes;
    private readonly SemaphoreSlim _gate;

    public LeetifyHttpProfileProvider(string apiKey)
        : this(SharedClient, apiKey)
    {
    }

    public LeetifyHttpProfileProvider(
        HttpClient client,
        string apiKey,
        Uri? baseUri = null,
        TimeSpan? timeout = null,
        int maxResponseBytes = DefaultMaxResponseBytes,
        int maxConcurrency = DefaultMaxConcurrency)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Length > 4096
            || apiKey.Any(character => character is '\r' or '\n'))
        {
            throw new ArgumentException(
                "A bounded Leetify API key is required.",
                nameof(apiKey));
        }

        _apiKey = apiKey;
        _baseUri = baseUri ?? DefaultBaseUri;
        if (!_baseUri.IsAbsoluteUri
            || !string.Equals(_baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Leetify API base URI must be absolute HTTPS.",
                nameof(baseUri));
        }

        _timeout = timeout ?? DefaultTimeout;
        if (_timeout < TimeSpan.FromMilliseconds(10)
            || _timeout > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Leetify timeout must be between 10 ms and 15 seconds.");
        }

        if (maxResponseBytes is < 1024 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        if (maxConcurrency is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _maxResponseBytes = maxResponseBytes;
        _gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async ValueTask<LeetifyLookupResult> ReadAsync(
        PlayerId player,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(_timeout);

            var requestUri = new Uri(
                _baseUri,
                "v3/profile?steam64_id="
                + player.SteamId64.ToString(CultureInfo.InvariantCulture));
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new LeetifyLookupResult(LeetifyLookupStatus.Timeout);
            }
            catch (HttpRequestException)
            {
                return new LeetifyLookupResult(LeetifyLookupStatus.Unavailable);
            }

            using (response)
            {
                var mapped = MapStatus(response.StatusCode);
                if (mapped is not null)
                    return new LeetifyLookupResult(mapped.Value);
                if (!response.IsSuccessStatusCode)
                    return new LeetifyLookupResult(LeetifyLookupStatus.Unavailable);

                byte[]? payload;
                try
                {
                    payload = await ReadBoundedAsync(
                        response.Content,
                        _maxResponseBytes,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new LeetifyLookupResult(LeetifyLookupStatus.Timeout);
                }
                catch (HttpRequestException)
                {
                    return new LeetifyLookupResult(LeetifyLookupStatus.Unavailable);
                }

                if (payload is null)
                    return new LeetifyLookupResult(LeetifyLookupStatus.InvalidResponse);

                return Parse(player, payload);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LeetifyLookupStatus? MapStatus(HttpStatusCode status)
        => status switch
        {
            HttpStatusCode.NotFound => LeetifyLookupStatus.NotFound,
            HttpStatusCode.TooManyRequests => LeetifyLookupStatus.RateLimited,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => LeetifyLookupStatus.Unauthorized,
            _ => null,
        };

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maximum,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximum)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream(
            content.Headers.ContentLength is > 0 and <= int.MaxValue
                ? (int)Math.Min(content.Headers.ContentLength.Value, maximum)
                : Math.Min(4096, maximum));
        var chunk = new byte[Math.Min(4096, maximum)];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > maximum)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static LeetifyLookupResult Parse(PlayerId player, byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("steam64_id", out var steam)
                || steam.ValueKind != JsonValueKind.String
                || !string.Equals(
                    steam.GetString(),
                    player.SteamId64.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                || !root.TryGetProperty("privacy_mode", out var privacy)
                || privacy.ValueKind != JsonValueKind.String)
            {
                return new LeetifyLookupResult(
                    LeetifyLookupStatus.InvalidResponse);
            }

            var privacyMode = privacy.GetString();
            if (string.Equals(privacyMode, "private", StringComparison.OrdinalIgnoreCase))
                return new LeetifyLookupResult(LeetifyLookupStatus.Private);
            if (!string.Equals(privacyMode, "public", StringComparison.OrdinalIgnoreCase))
                return new LeetifyLookupResult(LeetifyLookupStatus.InvalidResponse);

            if (!root.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString())
                || !root.TryGetProperty("rating", out var rating)
                || rating.ValueKind != JsonValueKind.Object)
            {
                return new LeetifyLookupResult(
                    LeetifyLookupStatus.InvalidResponse);
            }

            var metrics = new List<LeetifyMetric>(3);
            AddMetric(rating, "aim", "Aim", metrics);
            AddMetric(rating, "positioning", "Positioning", metrics);
            AddMetric(rating, "utility", "Utility", metrics);
            if (metrics.Count != 3)
                return new LeetifyLookupResult(LeetifyLookupStatus.InvalidResponse);

            return new LeetifyLookupResult(
                LeetifyLookupStatus.Available,
                new LeetifyProfileContext(
                    player,
                    name.GetString()!,
                    metrics.AsReadOnly(),
                    new Uri(
                        "https://leetify.com/app/profile/"
                        + player.SteamId64.ToString(CultureInfo.InvariantCulture))));
        }
        catch (JsonException)
        {
            return new LeetifyLookupResult(LeetifyLookupStatus.InvalidResponse);
        }
    }

    private static void AddMetric(
        JsonElement source,
        string property,
        string displayName,
        ICollection<LeetifyMetric> destination)
    {
        if (!source.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var raw = value.GetRawText();
        if (raw.Length is < 1 or > 64)
            return;
        destination.Add(new LeetifyMetric(displayName, raw));
    }
}
