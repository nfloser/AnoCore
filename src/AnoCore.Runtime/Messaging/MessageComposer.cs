using AnoCore.Abstractions.Localization;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Messaging;

public sealed class MessageComposer : IMessageComposer
{
    public const int MaximumOutputLength = 1024;

    private readonly ILocalizationService _localization;

    public MessageComposer(ILocalizationService localization)
        => _localization = localization ?? throw new ArgumentNullException(nameof(localization));

    public string Compose(MessageContent content, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var value = content.IsLocalized
            ? _localization.Translate(
                content.LocalizationKey!,
                locale,
                content.Arguments)
            : content.RawText!;

        var safe = new string(value
            .Select(character => char.IsControl(character) ? ' ' : character)
            .Take(MaximumOutputLength)
            .ToArray())
            .Trim();

        return safe;
    }
}

public sealed class FixedMessageLocaleResolver : IMessageLocaleResolver
{
    public FixedMessageLocaleResolver(string serverLocale)
    {
        if (string.IsNullOrWhiteSpace(serverLocale))
            throw new ArgumentException("A server locale is required.", nameof(serverLocale));
        ServerLocale = LocalizationConfiguration.NormalizeLocale(serverLocale);
    }

    public string ServerLocale { get; }

    public string Resolve(PlayerSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return ServerLocale;
    }
}

public sealed class MessageService : IMessageService
{
    private readonly IPlayerRegistry _players;
    private readonly IMessageComposer _composer;
    private readonly IMessageLocaleResolver _locales;
    private readonly IMessageTransport _transport;

    public MessageService(
        IPlayerRegistry players,
        IMessageComposer composer,
        IMessageLocaleResolver locales,
        IMessageTransport transport)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _locales = locales ?? throw new ArgumentNullException(nameof(locales));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async ValueTask<bool> SendAsync(
        PlayerId playerId,
        MessageChannel channel,
        MessageContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateChannel(channel);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_players.TryGet(playerId, out var player)
            || player is not { IsConnected: true })
            return false;

        var message = _composer.Compose(content, _locales.Resolve(player));
        if (message.Length == 0) return false;

        await _transport.SendAsync(player, channel, message, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async ValueTask<int> BroadcastAsync(
        MessageChannel channel,
        MessageContent content,
        CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var players = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64)
            .ToArray();
        var delivered = 0;
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = _composer.Compose(content, _locales.Resolve(player));
            if (message.Length == 0) continue;
            await _transport.SendAsync(player, channel, message, cancellationToken)
                .ConfigureAwait(false);
            delivered++;
        }

        return delivered;
    }

    private static void ValidateChannel(MessageChannel channel)
    {
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel));
    }
}
