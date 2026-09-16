namespace AnoCore.Abstractions.Players;

public sealed record PlayerSessionId
{
    public PlayerSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A player session id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PlayerSessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
