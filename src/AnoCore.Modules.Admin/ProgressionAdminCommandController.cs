using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Modules.Progression;

namespace AnoCore.Modules.Admin;

public sealed class ProgressionAdminCommandController : IDisposable
{
    private readonly List<IDisposable> _registrations = [];
    private readonly IModerationTargetGateway _targets;
    private readonly IProgressionAdministrationService _administration;
    private readonly Func<DateTimeOffset> _clock;
    private int _disposed;

    public ProgressionAdminCommandController(IAnoCommandRegistry commands, IModerationTargetGateway targets,
        IProgressionAdministrationService administration, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        try
        {
            Register(commands, ProgressionAdminOperation.Give, "anogivexp");
            Register(commands, ProgressionAdminOperation.Take, "anotakexp");
            Register(commands, ProgressionAdminOperation.Set, "anosetxp");
            Register(commands, ProgressionAdminOperation.Reset, "anoresetxp");
        }
        catch { Dispose(); throw; }
    }

    public static PermissionId Permission(ProgressionAdminOperation operation)
        => Enum.IsDefined(operation) ? new("ano.progression.xp." + operation.ToString().ToLowerInvariant())
            : throw new ArgumentOutOfRangeException(nameof(operation));

    private void Register(IAnoCommandRegistry commands, ProgressionAdminOperation operation, string name)
    {
        var arguments = new List<CommandArgumentDescriptor> { new("target", CommandArgumentKind.String, "One player or explicit offline SteamID64.") };
        if (operation != ProgressionAdminOperation.Reset)
            arguments.Add(new("amount", CommandArgumentKind.Int32, "XP amount (0-1000000000; give/take must be positive)."));
        arguments.Add(new("reason", CommandArgumentKind.String, "Administrative reason."));
        _registrations.Add(commands.Register(new ModuleId("ano.progression.admin"),
            new(name, "Adjust independent lifetime XP with audit.", Permission(operation), arguments: arguments),
            context => ExecuteAsync(operation, context)));
    }

    private async ValueTask<CommandResult> ExecuteAsync(ProgressionAdminOperation operation, CommandContext context)
    {
        if (Volatile.Read(ref _disposed) != 0) return CommandResult.Fail(CommandFailureReason.HandlerFailed);
        var target = await _targets.ResolveAsync(context.Get<string>("target"), context.Caller,
            Permission(operation), context.CancellationToken).ConfigureAwait(false);
        if (!target.Accepted || target.Target is null)
            return CommandResult.Fail(target.Failure is ModerationTargetFailure.PermissionDenied or ModerationTargetFailure.TargetImmune
                or ModerationTargetFailure.SelfTargetNotAllowed ? CommandFailureReason.Forbidden : CommandFailureReason.InvalidInput,
                "XP target rejected: " + target.Failure + ".");
        if (Volatile.Read(ref _disposed) != 0) return CommandResult.Fail(CommandFailureReason.HandlerFailed);
        try
        {
            var result = await _administration.ApplyAsync(new(Guid.NewGuid(), operation, target.Target.Id,
                operation == ProgressionAdminOperation.Reset ? 0 : context.Get<int>("amount"), context.Caller,
                context.Get<string>("reason"), _clock()), context.CancellationToken).ConfigureAwait(false);
            return CommandResult.Ok(string.Create(CultureInfo.InvariantCulture,
                $"Lifetime XP for {target.Target.Id}: {result.PreviousXp} -> {result.CurrentXp}. Request: {result.RequestId:D}."));
        }
        catch (ArgumentException exception) { return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        for (var index = _registrations.Count - 1; index >= 0; index--) _registrations[index].Dispose();
        _registrations.Clear();
    }
}
