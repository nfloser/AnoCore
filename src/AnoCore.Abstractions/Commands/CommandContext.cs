using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Commands;

public sealed record CommandContext(
    PlayerId? Caller,
    IReadOnlyList<string> Arguments,
    string RawInput,
    CancellationToken CancellationToken);
