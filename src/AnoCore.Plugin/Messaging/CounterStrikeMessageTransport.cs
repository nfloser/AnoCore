using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace AnoCore.Plugin.Messaging;

public sealed class CounterStrikeMessageTransport(IPlayerRegistry players) : IMessageTransport
{
    private readonly IPlayerRegistry _players = players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask<bool> TrySendAsync(
        MessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ScheduleAsync(() =>
        {
            var recipients = ResolveRecipients(request.Target);
            foreach (var player in recipients)
            {
                switch (request.Channel)
                {
                    case MessageChannel.Chat:
                        player.PrintToChat(NativeChatText.Prepare(request.Text));
                        break;
                    case MessageChannel.Center:
                        player.PrintToCenter(request.Text);
                        break;
                    case MessageChannel.CenterHtml:
                        if (request.Duration is { } duration)
                        {
                            player.PrintToCenterHtml(
                                request.Text,
                                Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)));
                        }
                        else
                        {
                            player.PrintToCenterHtml(request.Text);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(request), request.Channel, "Unsupported message channel.");
                }
            }

            return recipients.Count != 0;
        }, cancellationToken);
    }

    public ValueTask<bool> TryClearAsync(
        MessageTarget target,
        MessageChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (channel is MessageChannel.Chat)
        {
            return ValueTask.FromResult(false);
        }

        return ScheduleAsync(() =>
        {
            var recipients = ResolveRecipients(target);
            foreach (var player in recipients)
            {
                if (channel is MessageChannel.CenterHtml)
                {
                    player.PrintToCenterHtml(string.Empty, 1);
                }
                else
                {
                    player.PrintToCenter(string.Empty);
                }
            }

            return recipients.Count != 0;
        }, cancellationToken);
    }

    private async ValueTask<bool> ScheduleAsync(
        Func<bool> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        Server.NextWorldUpdate(() =>
        {
            if (completion.Task.IsCompleted)
            {
                return;
            }

            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return await completion.Task.ConfigureAwait(false);
    }

    private List<CCSPlayerController> ResolveRecipients(MessageTarget target)
    {
        var expected = target.Audience switch
        {
            MessageAudience.Player => ResolvePlayer(target),
            MessageAudience.Team => _players.OnlinePlayers
                .Where(player => player.IsConnected && player.Team == target.Team)
                .Select(player => player.Id.SteamId64)
                .ToHashSet(),
            MessageAudience.All => _players.OnlinePlayers
                .Where(player => player.IsConnected)
                .Select(player => player.Id.SteamId64)
                .ToHashSet(),
            _ => [],
        };

        if (expected.Count == 0)
        {
            return [];
        }

        return Utilities.GetPlayers()
            .Where(player =>
                player.IsValid
                && !player.IsBot
                && !player.IsHLTV
                && player.SteamID != 0
                && expected.Contains(player.SteamID))
            .ToList();
    }

    private HashSet<ulong> ResolvePlayer(MessageTarget target)
    {
        if (target.PlayerId is null
            || !_players.TryGet(target.PlayerId, out var player)
            || player is null
            || !player.IsConnected
            || (target.SessionId is not null && player.SessionId != target.SessionId))
        {
            return [];
        }

        return [player.Id.SteamId64];
    }
}
