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

    public NativeChatRouter(
        Func<PlayerId, ChatInterceptionDecision> moderation,
        IPlayerRegistry players,
        ChatSnapshotFormatter? formatter)
    {
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _formatter = formatter;
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
            return NativeChatRoute.PassThrough;

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

        return new NativeChatRoute(true, formatted, recipients);
    }
}
