using System.Globalization;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Progression;

public sealed class ChallengeNotificationService : IDisposable
{
    public static readonly PlayerSettingKey<bool> Preference = new("progression.challenge-notifications", true);
    public static readonly PlayerSettingKey<bool> ProgressPreference = new("progression.challenge-progress-notifications", false);
    private const int MaximumObservations = 32768;
    private readonly object _progressGate = new();
    private readonly Dictionary<(PlayerId Player, PlayerSessionId Session, string Id, int Version, DateTimeOffset Start),
        (long Count, DateTimeOffset End)> _observed = [];
    private readonly IPlayerRegistry _players;
    private readonly IPlayerSettingsService _settings;
    private readonly IMessageService _messages;
    private readonly Action<Exception>? _reportError;
    private readonly IDisposable _toggle;
    private readonly IDisposable _progressToggle;
    private int _disposed;

    public ChallengeNotificationService(IPlayerRegistry players, IPlayerSettingsService settings,
        IPlayerToggleCatalog toggles, IMessageService messages, Action<Exception>? reportError = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        ArgumentNullException.ThrowIfNull(toggles);
        _reportError = reportError;
        _toggle = toggles.Register(new ModuleId("ano.progression.challenges"),
            new PlayerToggleSetting(Preference, "Challenge notifications",
                "Show newly completed challenges and awarded XP in chat."));
        try
        {
            _progressToggle = toggles.Register(new ModuleId("ano.progression.challenges"),
                new PlayerToggleSetting(ProgressPreference, "Challenge progress notifications",
                    "Show observed progress increases before challenge completion."));
        }
        catch { _toggle.Dispose(); throw; }
    }

    // Only newly committed records belong here. Chat delivery is best effort and never replays rewards.
    public async ValueTask NotifyAsync(PlayerSnapshot player, string name,
        ChallengeCompletionResult result, CancellationToken cancellationToken = default)
    {
        if (!result.Applied || result.Completion is not { } completion
            || completion.Grant.PlayerId != player.Id || !Current(player)) return;
        try
        {
            if (!await _settings.GetAsync(player.Id, Preference, cancellationToken).ConfigureAwait(false)
                || !Current(player)) return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { Report(exception); return; }

        var safeName = new string(name.Where(character => !char.IsControl(character)
            && character is not '{' and not '}' and not '|').Take(48).ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current(player)) return;
        try
        {
            await _messages.SendAsync(new MessageRequest(
                MessageTarget.ForPlayer(player.Id, player.SessionId), MessageChannel.Chat,
                $"[ANO] Challenge completed: {safeName}. +{completion.Grant.AwardedXp.ToString(CultureInfo.InvariantCulture)} XP."),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { Report(exception); }
    }

    public void Prune(DateTimeOffset at)
    {
        lock (_progressGate)
        {
            foreach (var key in _observed.Keys.Where(key => _observed[key].End <= at
                || !_players.TryGet(key.Player, out var player) || player is not { IsConnected: true }
                || player.SessionId != key.Session).ToArray()) _observed.Remove(key);
        }
    }

    public async ValueTask ObserveProgressAsync(PlayerSnapshot player, ChallengeCompletionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!Current(player)) return;
        var evaluation = result.Evaluation;
        var definition = evaluation.Definition;
        var key = (player.Id, player.SessionId, definition.Id, definition.Version, definition.StartsAtUtc);
        bool increased;
        lock (_progressGate)
        {
            if (!Current(player)) return;
            if (result.Applied || evaluation.State is not ChallengeEvaluationState.Active
                || evaluation.Progress < 0 || evaluation.Progress >= evaluation.Target)
            {
                _observed.Remove(key);
                return;
            }
            if (_observed.Count >= MaximumObservations && !_observed.ContainsKey(key)) return;
            var hadPrevious = _observed.TryGetValue(key, out var previous);
            increased = hadPrevious && evaluation.Progress > previous.Count;
            _observed[key] = (hadPrevious ? Math.Max(previous.Count, evaluation.Progress) : evaluation.Progress, definition.EndsAtUtc);
        }
        if (!increased) return;
        // Advance the observation before delivery: preference/error changes must never catch up old progress.
        try
        {
            if (!await _settings.GetAsync(player.Id, ProgressPreference, cancellationToken).ConfigureAwait(false)
                || !Current(player)) return;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_progressGate)
                if (!_observed.TryGetValue(key, out var latest) || latest.Count != evaluation.Progress) return;
            var name = new string(definition.Name.Where(character => !char.IsControl(character)
                && character is not '{' and not '}' and not '|').Take(48).ToArray());
            await _messages.SendAsync(new MessageRequest(MessageTarget.ForPlayer(player.Id, player.SessionId),
                MessageChannel.Chat, string.Create(CultureInfo.InvariantCulture,
                    $"[ANO] Challenge progress: {name}. {evaluation.Progress}/{evaluation.Target}.")), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { Report(exception); }
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not affect durable progression. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _progressToggle.Dispose();
        _toggle.Dispose();
        lock (_progressGate) _observed.Clear();
    }
}
