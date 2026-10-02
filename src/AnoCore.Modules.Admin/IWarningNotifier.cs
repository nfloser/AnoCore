using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public interface IWarningNotifier
{
    void Notify(PlayerId targetId, PlayerSessionId sessionId, string message);
}
