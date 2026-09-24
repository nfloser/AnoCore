using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;

namespace AnoCore.Plugin.Moderation;

public sealed class CounterStrikeKickAnnouncement : IKickAnnouncement
{
    private readonly CancellationToken _lifetime;

    public CounterStrikeKickAnnouncement(CancellationToken lifetime)
        => _lifetime = lifetime;

    public async ValueTask AnnounceAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        await Server.NextWorldUpdateAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            Server.PrintToChatAll("[ANO] A player was kicked by an admin.");
        }).WaitAsync(token).ConfigureAwait(false);
    }
}
