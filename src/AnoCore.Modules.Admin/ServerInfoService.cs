using System.Globalization;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Admin;

public sealed class ServerInfoService : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly IMessageService _messages;
    private readonly IPlaytimeRepository _playtime;
    private readonly ServerInfoConfiguration _initial;
    private readonly Action<Exception>? _onFailure;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly Dictionary<PlayerId, PlayerSessionId> _welcomed = [];
    private IConfigReloadRegistration<ServerInfoConfiguration>? _reload;
    private ServerInfoConfiguration? _scheduledPolicy;
    private DateTimeOffset _nextInfo;
    private int _cursor;
    private int _busy;
    private int _disposed;

    private ServerInfoService(ServerInfoConfiguration initial, IPlayerRegistry players,
        IMessageService messages, IPlaytimeRepository playtime, Action<Exception>? onFailure)
    {
        _initial = initial;
        _players = players;
        _messages = messages;
        _playtime = playtime;
        _onFailure = onFailure;
        _token = _lifetime.Token;
    }

    public static async Task<ServerInfoService> CreateAsync(IConfigStore configuration,
        IPlayerRegistry players, IMessageService messages, IPlaytimeRepository playtime,
        IConfigReloadRegistry? reloads = null, Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(playtime);
        ValueTask<ServerInfoConfiguration> Load(CancellationToken token)
            => configuration.LoadAsync("server-info", () => new ServerInfoConfiguration(),
                ServerInfoConfiguration.Validate, token);
        var initial = await Load(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var service = new ServerInfoService(initial, players, messages, playtime, onFailure);
        try
        {
            service._reload = reloads?.Register(new ModuleId("ano.chat.info"), "server-info", initial,
                Load, ServerInfoConfiguration.Validate);
            return service;
        }
        catch { service.Dispose(); throw; }
    }

    // The native adapter captures map name on the server thread before calling this method.
    public async ValueTask TickAsync(DateTimeOffset now, string map,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
            var policy = _reload?.Current ?? _initial;
            if (!ReferenceEquals(_scheduledPolicy, policy) || !policy.Enabled)
            {
                _scheduledPolicy = policy;
                _nextInfo = now.AddSeconds(policy.IntervalSeconds);
                _cursor = 0;
            }
            if (!policy.Enabled) return;
            var online = _players.OnlinePlayers.Where(p => p.IsConnected).ToArray();
            var ids = online.Select(p => p.Id).ToHashSet();
            foreach (var id in _welcomed.Keys.Where(id => !ids.Contains(id)).ToArray()) _welcomed.Remove(id);
            var infoDue = now >= _nextInfo;
            List<string>? info = null;
            if (infoDue)
            {
                _nextInfo = now.AddSeconds(policy.IntervalSeconds); // no catch-up flood after pauses
                if (policy.InfoMessages.Count > 0)
                {
                    info = policy.InfoMessages[_cursor % policy.InfoMessages.Count];
                    _cursor = (_cursor + 1) % policy.InfoMessages.Count;
                }
            }
            foreach (var player in online)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(player)) continue;
                var welcomeDue = now >= player.ConnectedAtUtc.AddSeconds(policy.WelcomeDelaySeconds)
                    && (!_welcomed.TryGetValue(player.Id, out var session) || session != player.SessionId);
                if (welcomeDue) _welcomed[player.Id] = player.SessionId;
                if (!welcomeDue && info is null) continue;
                try
                {
                    var lines = (welcomeDue ? policy.WelcomeMessages : []).Concat(info ?? []).ToArray();
                    var total = "nicht verfügbar";
                    if (lines.Any(line => line.Contains("{playtime.total}", StringComparison.Ordinal)))
                    {
                        try
                        {
                            var totals = await _playtime.ReadAsync(player.Id,
                                DateOnly.FromDateTime(now.UtcDateTime), linked.Token).ConfigureAwait(false);
                            var duration = totals.Total < TimeSpan.Zero ? TimeSpan.Zero : totals.Total;
                            total = string.Create(CultureInfo.InvariantCulture,
                                $"{(long)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m");
                        }
                        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                        catch (Exception exception) { Report(exception); }
                    }
                    foreach (var line in lines)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (!IsCurrent(player) || !ReferenceEquals(policy, _reload?.Current ?? _initial)) break;
                        await _messages.SendAsync(new MessageRequest(
                            MessageTarget.ForPlayer(player.Id, player.SessionId), MessageChannel.Chat,
                            ServerInfoText.Render(line, player, map, total)), linked.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception exception) { Report(exception); }
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private bool IsCurrent(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void Report(Exception exception)
    {
        try { _onFailure?.Invoke(exception); }
        catch { /* Diagnostics must not interrupt other recipients. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _reload?.Dispose();
        _lifetime.Dispose();
    }
}
