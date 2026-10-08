using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class CssTeamAdministrationPolicyTests
{
    [TestMethod]
    public void Authorization_RequiresDedicatedCssFlagAndTargetImmunity()
    {
        Assert.AreEqual(CommandFailureReason.Forbidden, CssTeamAdministrationPolicy.Authorize(false)!.FailureReason);
        Assert.IsNull(CssTeamAdministrationPolicy.Authorize(true));
        Assert.AreEqual(CommandFailureReason.Forbidden, CssTeamAdministrationPolicy.CheckTarget(1, false)!.FailureReason);
        Assert.IsNull(CssTeamAdministrationPolicy.CheckTarget(1, true));
        Assert.AreEqual(CommandFailureReason.InvalidInput, CssTeamAdministrationPolicy.CheckTarget(0, true)!.FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, CssTeamAdministrationPolicy.CheckTarget(2, true)!.FailureReason);
    }

    [TestMethod]
    [DataRow("t", PlayerTeam.Terrorist)]
    [DataRow("CT", PlayerTeam.CounterTerrorist)]
    [DataRow("spec", PlayerTeam.Spectator)]
    [DataRow("spectator", PlayerTeam.Spectator)]
    public void Destination_MapsExplicitTeamsWithoutGlobalSideSwap(string text, PlayerTeam expected)
    {
        Assert.IsTrue(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SetTeam, text,
            PlayerTeam.Unknown, out var team));
        Assert.AreEqual(expected, team);
    }

    [TestMethod]
    public void Destination_SwapsOnlyAnIndividualActiveTeamAndRejectsOtherOperations()
    {
        Assert.IsTrue(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SwapTeam, null, PlayerTeam.Terrorist, out var team));
        Assert.AreEqual(PlayerTeam.CounterTerrorist, team);
        Assert.IsTrue(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SwapTeam, null, PlayerTeam.CounterTerrorist, out team));
        Assert.AreEqual(PlayerTeam.Terrorist, team);
        Assert.IsFalse(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SwapTeam, null, PlayerTeam.Spectator, out _));
        Assert.IsFalse(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SetTeam, "t;quit", PlayerTeam.Terrorist, out _));
        Assert.IsFalse(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.SetTeam, "none", PlayerTeam.Terrorist, out _));
        Assert.IsFalse(CssTeamAdministrationPolicy.TryDestination(ExtendedInventoryTeamOperation.Strip, "ct", PlayerTeam.Terrorist, out _));
    }
}
