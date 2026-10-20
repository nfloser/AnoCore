using System.Globalization;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Presentation of virtual wallet units; no exchange rate or real-money settlement.</summary>
public static class CasinoMoney
{
    // Leaves room for multiple account components and reserved exposure in signed BIGINT arithmetic.
    public const long AccountLimit = 1_000_000_000_000_000_000;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] Units = ["", "Tsd.", "Mio.", "Mrd.", "Bio.", "Brd.", "Trill."];

    public static string FormatExact(long value) => value.ToString("N0", German) + " €";

    public static string Format(long value)
    {
        var scaled = (decimal)value;
        var unit = 0;
        while (Math.Abs(scaled) >= 1000 && unit < Units.Length - 1)
        {
            scaled /= 1000;
            unit++;
        }
        if (unit == 0) return FormatExact(value);
        scaled = decimal.Round(scaled, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(scaled) >= 1000 && unit < Units.Length - 1)
        {
            scaled /= 1000;
            unit++;
        }
        return scaled.ToString("0.##", German) + " " + Units[unit] + " €";
    }
}
