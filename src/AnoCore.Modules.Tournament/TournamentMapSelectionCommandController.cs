using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public sealed class TournamentMapSelectionCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.tournament.veto");
    private readonly TournamentMapSelectionService _selection;
    private readonly List<IDisposable> _registrations = [];
    private int _disposed;

    public TournamentMapSelectionCommandController(
        IAnoCommandRegistry commands,
        TournamentMapSelectionService selection)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        var permission = TournamentCommandController.GetManagePermission();
        try
        {
            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anotournamentveto",
                    "Start AnoVeto for the active ready tournament match.",
                    permission),
                StartAsync));
            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anotournamentvetocomplete",
                    "Commit the completed AnoVeto result as the tournament map series.",
                    permission),
                CompleteAsync));
            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anotournamentvetocancel",
                    "Cancel the active tournament AnoVeto selection.",
                    permission),
                CancelAsync));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private async ValueTask<CommandResult> StartAsync(CommandContext context)
    {
        if (context.Caller is null)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A player manager is required to start tournament AnoVeto.");
        try
        {
            await _selection.StartAsync(context.Caller, context.CancellationToken)
                .ConfigureAwait(false);
            return CommandResult.Ok("[ANO] Tournament AnoVeto started.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }
    }

    private async ValueTask<CommandResult> CompleteAsync(CommandContext context)
    {
        if (context.Caller is null)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A player manager is required to complete tournament AnoVeto.");
        try
        {
            var result = await _selection.CompleteAsync(
                context.Caller, context.CancellationToken).ConfigureAwait(false);
            return CommandResult.Ok(
                $"[ANO] Tournament maps committed: {string.Join(", ", result.Maps)}.");
        }
        catch (TournamentConcurrencyException exception)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, exception.Message);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }
    }

    private async ValueTask<CommandResult> CancelAsync(CommandContext context)
    {
        if (context.Caller is null)
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "A player manager is required to cancel tournament AnoVeto.");
        try
        {
            await _selection.CancelAsync(context.Caller, context.CancellationToken)
                .ConfigureAwait(false);
            return CommandResult.Ok("[ANO] Tournament AnoVeto cancelled.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        for (var index = _registrations.Count - 1; index >= 0; index--)
            _registrations[index].Dispose();
        _registrations.Clear();
        _selection.Dispose();
    }
}
