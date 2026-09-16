namespace AnoCore.Abstractions.Commands;

public delegate ValueTask<CommandResult> AnoCommandHandler(CommandContext context);
