namespace AnoCore.Abstractions.Commands;

public enum CommandFailureReason
{
    None = 0,
    NotFound = 1,
    Forbidden = 2,
    InvalidInput = 3,
    HandlerFailed = 4,
}
