using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public delegate bool ChatSnapshotFormatter(
    PlayerId playerId,
    PlayerSessionId sessionId,
    string? message,
    bool isTeamMessage,
    out string? formatted);

public sealed record NativeChatRoute(
    bool ShouldIntercept,
    string? FormattedMessage,
    IReadOnlyCollection<PlayerId> Recipients)
{
    public static NativeChatRoute PassThrough { get; } =
        new(false, null, Array.Empty<PlayerId>());

    public static NativeChatRoute Suppress { get; } =
        new(true, null, Array.Empty<PlayerId>());
}

public sealed class NativeChatRouter
{
    private readonly Func<PlayerId, ChatInterceptionDecision> _moderation;
    private readonly IPlayerRegistry _players;
    private readonly ChatSnapshotFormatter? _formatter;
    private readonly IAnoEventBus? _events;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Exception>? _reportError;

    public NativeChatRouter(
        Func<PlayerId, ChatInterceptionDecision> moderation,
        IPlayerRegistry players,
        ChatSnapshotFormatter? formatter,
        IAnoEventBus? events = null,
        Func<DateTimeOffset>? clock = null,
        Action<Exception>? reportError = null)
    {
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _formatter = formatter;
        _events = events;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reportError = reportError;
    }

    public NativeChatRoute Route(
        PlayerId senderId,
        string? message,
        bool isTeamMessage)
    {
        ArgumentNullException.ThrowIfNull(senderId);

        var input = message?.TrimStart() ?? string.Empty;
        if (input.StartsWith('!') || input.StartsWith('/'))
            return NativeChatRoute.PassThrough;

        if (string.IsNullOrWhiteSpace(input))
            return NativeChatRoute.Suppress;

        if (_moderation(senderId) != ChatInterceptionDecision.Allow)
            return NativeChatRoute.Suppress;

        if (_formatter is null)
        {
            if (_players.TryGet(senderId, out var nativeSender) && nativeSender is { IsConnected: true })
                Notify(nativeSender, message!, isTeamMessage);
            return NativeChatRoute.PassThrough;
        }

        if (!_players.TryGet(senderId, out var sender)
            || sender is null
            || !sender.IsConnected
            || !_formatter(
                sender.Id,
                sender.SessionId,
                message,
                isTeamMessage,
                out var formatted)
            || string.IsNullOrWhiteSpace(formatted))
        {
            return NativeChatRoute.Suppress;
        }

        var recipients = _players.OnlinePlayers
            .Where(player => player.IsConnected
                && (!isTeamMessage || player.Team == sender.Team))
            .Select(player => player.Id)
            .Distinct()
            .ToArray();

        var route = new NativeChatRoute(true, formatted, recipients);
        Notify(sender, message!, isTeamMessage);
        return route;
    }

    private void Notify(PlayerSnapshot sender, string message, bool isTeamMessage)
    {
        if (_events is null) return;
        _ = PublishAsync(sender, message.Length <= 1024 ? message : message[..1024], isTeamMessage);
    }

    private async Task PublishAsync(PlayerSnapshot sender, string message, bool isTeamMessage)
    {
        try
        {
            await _events!.PublishAsync(new PlayerChatAcceptedEvent(sender, message, isTeamMessage,
                _clock().ToUniversalTime())).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try { _reportError?.Invoke(exception); }
            catch { }
        }
    }
}
