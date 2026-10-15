using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoMoneyTests
{
    [TestMethod]
    [DataRow(0L, "0 €")]
    [DataRow(999L, "999 €")]
    [DataRow(5000L, "5 Tsd. €")]
    [DataRow(125000L, "125 Tsd. €")]
    [DataRow(1500000L, "1,5 Mio. €")]
    [DataRow(75000000L, "75 Mio. €")]
    [DataRow(1250000000L, "1,25 Mrd. €")]
    [DataRow(1000000000000L, "1 Bio. €")]
    [DataRow(1000000000000000L, "1 Brd. €")]
    [DataRow(999999L, "1 Mio. €")]
    [DataRow(-1500000L, "-1,5 Mio. €")]
    [DataRow(long.MinValue, "-9,22 Trill. €")]
    public void BalancesAndWinsUseGermanShortUnits(long amount, string expected)
        => Assert.AreEqual(expected, CasinoMoney.Format(amount));

    [TestMethod]
    public void StakesRetainAllDigits()
    {
        Assert.AreEqual("100.000 €", CasinoMoney.FormatExact(100_000));
        Assert.AreEqual("1.000.000 €", CasinoMoney.FormatExact(1_000_000));
    }
}
