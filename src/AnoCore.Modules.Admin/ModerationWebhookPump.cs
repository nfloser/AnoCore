using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Admin;

public sealed class ModerationWebhookConfiguration
{
    public bool Enabled { get; set; }
    public List<string> AllowedHosts { get; set; } = [];
    public int PollSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 32;
    public bool IncludeTargetIds { get; set; }
    public bool IncludeReasons { get; set; }
    public static IReadOnlyCollection<string> Validate(ModerationWebhookConfiguration value)
    {
        if (value is null || value.PollSeconds is < 1 or > 60 || value.TimeoutSeconds is < 1 or > 10 || value.BatchSize is < 1 or > 64
            || value.AllowedHosts is null || value.AllowedHosts.Count > 16
            || value.AllowedHosts.Any(host => Uri.CheckHostName(host) != UriHostNameType.Dns || host.Length > 253)
            || value.Enabled && value.AllowedHosts.Count == 0)
            return ["Webhook settings require bounded intervals/batch and explicitly approved DNS hosts."];
        return [];
    }
}

public static class WebhookDestinationPolicy
{
    public static Uri ValidateEndpoint(string? text, ModerationWebhookConfiguration options)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.Port != 443 || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0
            || Uri.CheckHostName(endpoint.Host) != UriHostNameType.Dns
            || !options.AllowedHosts.Contains(endpoint.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Webhook endpoint must use HTTPS and an approved public DNS host.");
        return endpoint;
    }
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 &&
                (bytes[2] == 0 && bytes[3] == 0 || bytes[2] == 0x0d && bytes[3] == 0xb8))
                && !(bytes[0] == 0x20 && bytes[1] == 0x02);
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254)
            && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0))
            && !(bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100))
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }

    public static HttpClient CreateClient(int timeoutSeconds)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 8,
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
                    throw new HttpRequestException("Webhook DNS resolved outside public address space.");
                var address = addresses[0];
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
        };
        return new(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }
}

public sealed record CommittedAdminAudit(string Id, string Action, DateTimeOffset OccurredAtUtc, string? Target, string Reason);

/// <summary>Bounded, best-effort post-commit notifications. No retry/outbox or replay on restart.</summary>
public sealed class ModerationWebhookPump : IDisposable
{
    private readonly ModerationWebhookConfiguration _options;
    private readonly Func<DateTimeOffset, int, CancellationToken, ValueTask<IReadOnlyList<CommittedAdminAudit>>> _read;
    private readonly Func<string, CancellationToken, ValueTask> _send;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _checkpoint = new(1, 1);
    private readonly HashSet<string> _seen = [];
    private readonly Queue<string> _order = new();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly HttpClient? _client;
    private Task? _worker;
    private int _running;
    private long _failures;
    private int _disposed;
    public long Failures => Interlocked.Read(ref _failures);

    public ModerationWebhookPump(ModerationWebhookConfiguration options,
        Func<DateTimeOffset, int, CancellationToken, ValueTask<IReadOnlyList<CommittedAdminAudit>>> read,
        Func<string, CancellationToken, ValueTask> send, HttpClient? client = null)
    {
        if (ModerationWebhookConfiguration.Validate(options).Count > 0)
            throw new ArgumentException("Invalid webhook policy.", nameof(options));
        _options = new()
        {
            Enabled = options.Enabled,
            AllowedHosts = [.. options.AllowedHosts],
            PollSeconds = options.PollSeconds,
            TimeoutSeconds = options.TimeoutSeconds,
            BatchSize = options.BatchSize,
            IncludeTargetIds = options.IncludeTargetIds,
            IncludeReasons = options.IncludeReasons,
        };
        _read = read;
        _send = send;
        _client = client;
    }

    public static ModerationWebhookPump Create(IDatabase database, ModerationWebhookConfiguration options)
    {
        if (!options.Enabled) return new(options, (_, _, _) => ValueTask.FromResult<IReadOnlyList<CommittedAdminAudit>>([]), (_, _) => ValueTask.CompletedTask);
        var endpoint = WebhookDestinationPolicy.ValidateEndpoint(Environment.GetEnvironmentVariable("ANOCORE_MODERATION_WEBHOOK_URL"), options);
        var client = WebhookDestinationPolicy.CreateClient(options.TimeoutSeconds);
        return new(options, (since, limit, token) => ReadCommittedAsync(database, since, limit, token), async (payload, token) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var secret = Environment.GetEnvironmentVariable("ANOCORE_MODERATION_WEBHOOK_TOKEN");
            if (!string.IsNullOrEmpty(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
        }, client);
    }

    public void Start()
    {
        if (_options.Enabled && Volatile.Read(ref _disposed) == 0 && Interlocked.CompareExchange(ref _running, 1, 0) == 0) _worker = Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));
            while (await timer.WaitForNextTickAsync(_lifetime.Token)) await CheckpointAsync(DateTimeOffset.UtcNow, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    public async ValueTask CheckpointAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || Volatile.Read(ref _disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _checkpoint.WaitAsync(0, linked.Token)) return;
        try
        {
            var since = now.AddSeconds(-60) > _started ? now.AddSeconds(-60) : _started;
            var batch = await _read(since, _options.BatchSize, linked.Token);
            foreach (var item in batch.Take(_options.BatchSize))
            {
                linked.Token.ThrowIfCancellationRequested();
                if (item.OccurredAtUtc < since || item.OccurredAtUtc > now || !_seen.Add(item.Id)) continue;
                _order.Enqueue(item.Id);
                while (_order.Count > 4096) _seen.Remove(_order.Dequeue());
                try { await _send(Payload(item, _options), linked.Token); }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch { Interlocked.Increment(ref _failures); }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch { Interlocked.Increment(ref _failures); }
        finally { _checkpoint.Release(); }
    }
    public static string Payload(CommittedAdminAudit item, ModerationWebhookConfiguration options)
    {
        static string Clean(string text, int limit) => new(text.Where(value => !char.IsControl(value)).Take(limit).ToArray());
        var content = $"[AnoCore] {Clean(item.Action, 64)} · {item.OccurredAtUtc:O}";
        if (options.IncludeTargetIds) content += " · target=" + Clean(item.Target ?? "none", 32);
        if (options.IncludeReasons) content += " · " + Clean(item.Reason, 512);
        return JsonSerializer.Serialize(new { content, allowed_mentions = new { parse = Array.Empty<string>() } });
    }

    public static ValueTask<IReadOnlyList<CommittedAdminAudit>> ReadCommittedAsync(IDatabase database,
        DateTimeOffset since, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.WithConnectionAsync<IReadOnlyList<CommittedAdminAudit>>(async (connection, cancellation) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, action, occurred_at_utc, target, reason FROM (
                    SELECT CONCAT('admin:', audit_id) id, action_id action, occurred_at_utc,
                        target_steam_id target, reason FROM ano_admin_action_audit WHERE occurred_at_utc >= @since
                    UNION ALL
                    SELECT CONCAT('moderation:', audit_id), CONCAT('moderation.', action, '.', restrictions),
                        occurred_at_utc, target_steam_id, reason FROM ano_moderation_audit WHERE occurred_at_utc >= @since
                ) committed ORDER BY occurred_at_utc DESC, id DESC LIMIT @limit
                """;
            foreach (var (name, value) in new (string, object)[] { ("@since", since.UtcDateTime), ("@limit", limit) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            var items = new List<CommittedAdminAudit>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) items.Add(new(reader.GetString(0), reader.GetString(1),
                new(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)),
                reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture), reader.GetString(4)));
            return items;
        }, token);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _client?.Dispose();
    }
}
