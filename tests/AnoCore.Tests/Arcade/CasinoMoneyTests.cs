using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoMoneyTests
{
    [TestMethod]
    public void EuroFormattingPreservesTheVirtualIntegerAmount()
    {
        Assert.AreEqual("0 €", CasinoMoney.Format(0));
        Assert.AreEqual("100 €", CasinoMoney.Format(100));
        Assert.AreEqual("10.000 €", CasinoMoney.Format(10_000));
        Assert.AreEqual("-250 €", CasinoMoney.Format(-250));
    }
}
