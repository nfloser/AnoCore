using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Contracts;

[TestClass]
public sealed class ValueObjectTests
{
    [TestMethod]
    public void ModuleId_NormalizesWhitespaceAndCase()
    {
        var id = new ModuleId("  Ano.Veto  ");

        Assert.AreEqual("ano.veto", id.Value);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("ano veto")]
    [DataRow("ano/veto")]
    public void ModuleId_RejectsInvalidValues(string value)
    {
        Assert.ThrowsException<ArgumentException>(() => _ = new ModuleId(value));
    }

    [TestMethod]
    public void PermissionId_RequiresAnoNamespaceAndNormalizesCase()
    {
        var permission = new PermissionId("  ANO.VETO.CREATE ");

        Assert.AreEqual("ano.veto.create", permission.Value);
        Assert.ThrowsException<ArgumentException>(() => _ = new PermissionId("zenith.admin"));
    }

    [TestMethod]
    public void PlayerId_RejectsZeroSteamId()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = new PlayerId(0));
    }

    [TestMethod]
    public void CommandDescriptor_NormalizesCommandAndAliases()
    {
        var descriptor = new CommandDescriptor(
            "!AnoVeto",
            "Starts or opens the map veto.",
            new PermissionId("ano.veto.use"),
            ["anov", "!Veto"]);

        Assert.AreEqual("anoveto", descriptor.Name);
        CollectionAssert.AreEqual(new[] { "anov", "veto" }, descriptor.Aliases.ToArray());
    }
}
