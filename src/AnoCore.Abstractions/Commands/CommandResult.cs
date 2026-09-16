namespace AnoCore.Abstractions.Commands;

public sealed record CommandResult(
    bool Success,
    string? Message,
    CommandFailureReason FailureReason)
{
    public static CommandResult Ok(string? message = null) => new(true, message, CommandFailureReason.None);

    public static CommandResult Fail(CommandFailureReason reason, string? message = null)
    {
        if (reason == CommandFailureReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "A failed command requires a failure reason.");
        }

        return new CommandResult(false, message, reason);
    }
}
