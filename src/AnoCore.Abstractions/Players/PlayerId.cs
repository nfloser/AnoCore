namespace AnoCore.Abstractions.Players;

public readonly record struct PlayerId
{
    public PlayerId(ulong steamId64)
    {
        if (steamId64 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steamId64), "SteamID64 must be non-zero.");
        }

        SteamId64 = steamId64;
    }

    public ulong SteamId64 { get; }

    public override string ToString() => SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
