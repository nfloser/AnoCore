using AnoCore.Modules.AnoVeto;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoPresetTests
{
    private static string[] Maps() => Enumerable.Range(1, 8).Select(i => $"de_map{i}").ToArray();

    [TestMethod]
    public void Preset_CopiesEightUniqueIdsAndPreservesOwnership()
    {
        var source = Maps();
        var preset = new AnoVetoPreset(76561198000000001, " Competitive ", source);
        source[0] = "modified";
        Assert.AreEqual("Competitive", preset.Name);
        Assert.AreEqual("de_map1", preset.MapIds[0]);
        Assert.AreEqual((ulong)76561198000000001, preset.OwnerSteamId64);
        Assert.HasCount(8, preset.MapIds);
    }

    [TestMethod]
    public void Preset_RejectsMalformedDefinitions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AnoVetoPreset(0, "valid", Maps()));
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoPreset(1, " ", Maps()));
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoPreset(1, "bad\nname", Maps()));
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoPreset(1, "valid", Maps().Take(7)));
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoPreset(1, "valid", Maps().Take(7).Append("DE_MAP1")));
    }

    [TestMethod]
    public void RenameAndReplace_ReturnNewPresetsWithoutMutatingOldOne()
    {
        var original = new AnoVetoPreset(1, "Original", Maps());
        var renamed = original.Rename("Second");
        var replaced = renamed.ReplaceMaps(Enumerable.Range(9, 8).Select(i => $"de_map{i}"));
        Assert.AreEqual("Original", original.Name);
        Assert.AreEqual("Second", replaced.Name);
        Assert.AreEqual("de_map1", original.MapIds[0]);
        Assert.AreEqual("de_map9", replaced.MapIds[0]);
        Assert.AreEqual(original.OwnerSteamId64, replaced.OwnerSteamId64);
    }
}
