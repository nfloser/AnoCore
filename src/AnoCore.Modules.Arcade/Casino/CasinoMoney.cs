using System.Globalization;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Presentation of virtual wallet units; no exchange rate or real-money settlement.</summary>
public static class CasinoMoney
{
    public static string Format(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " €";
}
