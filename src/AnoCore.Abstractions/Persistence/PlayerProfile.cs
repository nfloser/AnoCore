using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Persistence;

public sealed record PlayerProfile
{
    public PlayerProfile(
        PlayerId id,
        string lastKnownName,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastSeenUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (lastSeenUtc < firstSeenUtc)
        {
            throw new ArgumentException("Last seen time cannot precede first seen time.", nameof(lastSeenUtc));
        }

        Id = id;
        LastKnownName = string.IsNullOrWhiteSpace(lastKnownName) ? "Unknown" : lastKnownName.Trim();
        FirstSeenUtc = firstSeenUtc.ToUniversalTime();
        LastSeenUtc = lastSeenUtc.ToUniversalTime();
    }

    public PlayerId Id { get; }

    public string LastKnownName { get; }

    public DateTimeOffset FirstSeenUtc { get; }

    public DateTimeOffset LastSeenUtc { get; }
}
