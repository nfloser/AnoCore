using AnoCore.Abstractions.Hud;

namespace AnoCore.Tests.Hud;

[TestClass]
public sealed class CustomHudContractTests
{
    [TestMethod]
    public void Definition_AllowsInformationOnlyLayoutWithoutInputCapture()
    {
        var definition = new CustomHudDefinition(
            new CustomHudId("ano.tournament.bracket"),
            "panorama/layout/custom_game/anocore/tournament_bracket.xml",
            "ano_tournament_root");

        Assert.AreEqual("ano.tournament.bracket", definition.Id.Value);
        Assert.AreEqual("panorama/layout/custom_game/anocore/tournament_bracket.xml", definition.LayoutResource);
        Assert.AreEqual("ano_tournament_root", definition.RootPanelId);
        Assert.AreEqual("shown", definition.VisibleClass);
        Assert.IsFalse(definition.CaptureInput);
        Assert.IsEmpty(definition.ButtonIds);
    }

    [TestMethod]
    public void Definition_SupportsInteractiveLayoutWithKnownButtons()
    {
        var definition = new CustomHudDefinition(
            new CustomHudId("ano.veto"),
            "panorama/layout/custom_game/anocore/ano_veto.xml",
            "ano_veto_root",
            ["ano_veto_map_0", "ano_veto_map_1", "ano_veto_close"],
            captureInput: true);

        Assert.IsTrue(definition.CaptureInput);
        CollectionAssert.AreEquivalent(
            new[] { "ano_veto_map_0", "ano_veto_map_1", "ano_veto_close" },
            definition.ButtonIds.ToArray());
    }

    [TestMethod]
    public void Definition_RejectsDuplicateButtonIds()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CustomHudDefinition(
            new CustomHudId("ano.veto"),
            "panorama/layout/custom_game/anocore/ano_veto.xml",
            "ano_veto_root",
            ["ano_veto_map_0", "ano_veto_map_0"],
            captureInput: true));
    }

    [TestMethod]
    public void Definition_RejectsNonPanoramaLayoutResource()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CustomHudDefinition(
            new CustomHudId("ano.veto"),
            "ano_veto.xml",
            "ano_veto_root"));
    }
}
