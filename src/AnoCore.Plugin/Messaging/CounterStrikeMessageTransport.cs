using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using CounterStrikeSharp.API;

namespace AnoCore.Plugin.Messaging;

public sealed class CounterStrikeMessageTransport : IMessageTransport, IDisposable
{
    private readonly IPlayerRegistry _players;
    private int _disposed;

    public CounterStrikeMessageTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public async ValueTask SendAsync(
        PlayerSnapshot expectedPlayer,
        MessageChannel channel,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPlayer);
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A message is required.", nameof(message));
        if (message.Length > AnoCore.Runtime.Messaging.MessageComposer.MaximumOutputLength)
            throw new ArgumentException("Message exceeds the supported output length.", nameof(message));
        if (message.Any(char.IsControl))
            throw new ArgumentException("Messages cannot contain control characters.", nameof(message));

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await Server.NextWorldUpdateAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0
                || !_players.TryGet(expectedPlayer.Id, out var current)
                || current is not { IsConnected: true }
                || current.SessionId != expectedPlayer.SessionId)
            {
                return;
            }

            var controller = Utilities.GetPlayers().FirstOrDefault(candidate =>
                candidate is { IsValid: true, IsBot: false, IsHLTV: false }
                && candidate.SteamID == expectedPlayer.Id.SteamId64);
            if (controller is null) return;

            switch (channel)
            {
                case MessageChannel.Chat:
                    controller.PrintToChat(message);
                    break;
                case MessageChannel.Center:
                    controller.PrintToCenter(message);
                    break;
                case MessageChannel.Console:
                    controller.PrintToConsole(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
