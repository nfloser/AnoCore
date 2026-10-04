using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchStateMachineTests
{
    private static readonly PlayerId A1 = new(76561198000186001);
    private static readonly PlayerId A2 = new(76561198000186002);
    private static readonly PlayerId B1 = new(76561198000186011);
    private static readonly PlayerId B2 = new(76561198000186012);

    [TestMethod]
    public void ReadyVetoKnifeAndSideChoice_FollowGuardedLifecycle()
    {
        var machine = NewMachine(TournamentBestOf.Three);

        machine.OpenReady();
        Assert.IsFalse(machine.Ready(A1));
        Assert.IsFalse(machine.Ready(A2));
        Assert.IsFalse(machine.Ready(B1));
        Assert.IsTrue(machine.Ready(B2));
        Assert.ThrowsExactly<InvalidOperationException>(() => machine.OpenReady());

        machine.BeginVeto();
        machine.CompleteVeto(["de_dust2", "de_nuke", "de_inferno"]);
        Assert.AreEqual(TournamentMatchState.Knife, machine.State);

        machine.CompleteKnife(TournamentTeamSlot.TeamB);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.Terrorist));

        machine.ChooseSide(TournamentTeamSlot.TeamB, PlayerTeam.Terrorist);
        Assert.AreEqual(TournamentMatchState.Live, machine.State);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, machine.AssignedSide(A1));
        Assert.AreEqual(PlayerTeam.Terrorist, machine.AssignedSide(B1));
    }

    [TestMethod]
    public void BestOfThree_CompletesAtTwoMapWinsAndResetsKnifePerMap()
    {
        var machine = LiveMachine(TournamentBestOf.Three);

        Assert.IsFalse(machine.CompleteMap(TournamentTeamSlot.TeamA));
        Assert.AreEqual(1, machine.TeamAMaps);
        Assert.AreEqual(1, machine.CurrentMapIndex);
        Assert.AreEqual(TournamentMatchState.Knife, machine.State);

        machine.CompleteKnife(TournamentTeamSlot.TeamA);
        machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.CounterTerrorist);
        Assert.IsTrue(machine.CompleteMap(TournamentTeamSlot.TeamA));

        Assert.AreEqual(TournamentMatchState.Completed, machine.State);
        Assert.AreEqual(2, machine.TeamAMaps);
        Assert.AreEqual(0, machine.TeamBMaps);
    }

    [TestMethod]
    public void PauseResumeAndOvertime_PreserveLiveState()
    {
        var machine = LiveMachine(TournamentBestOf.One);

        machine.Pause();
        Assert.AreEqual(TournamentMatchState.Paused, machine.State);
        machine.Resume();
        Assert.AreEqual(TournamentMatchState.Live, machine.State);

        machine.EnterOvertime();
        machine.Pause();
        var snapshot = machine.Snapshot();
        var restored = TournamentMatchStateMachine.Restore(machine.Configuration, snapshot);

        Assert.AreEqual(TournamentMatchState.Paused, restored.State);
        restored.Resume();
        Assert.AreEqual(TournamentMatchState.Overtime, restored.State);
    }

    [TestMethod]
    public void Restore_PreservesRosterAssignmentsReadinessMapsAndScore()
    {
        var machine = NewMachine(TournamentBestOf.Three);
        machine.OpenReady();
        foreach (var player in new[] { A1, A2, B1, B2 })
            machine.Ready(player);
        machine.BeginVeto();
        machine.CompleteVeto(["de_mirage", "de_ancient", "de_anubis"]);
        machine.CompleteKnife(TournamentTeamSlot.TeamA);
        machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.CounterTerrorist);
        machine.CompleteMap(TournamentTeamSlot.TeamB);

        var restored = TournamentMatchStateMachine.Restore(
            machine.Configuration, machine.Snapshot());

        Assert.AreEqual(1, restored.TeamBMaps);
        Assert.AreEqual(1, restored.CurrentMapIndex);
        Assert.AreEqual(TournamentMatchState.Knife, restored.State);
        Assert.IsTrue(restored.IsReady);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, restored.AssignedSide(A2));
        Assert.AreEqual(PlayerTeam.Terrorist, restored.AssignedSide(B2));
    }

    [TestMethod]
    public void Configuration_RejectsOverlappingRostersAndInvalidSeries()
    {
        var teamA = new TournamentTeam("Alpha", "A", A1, [A1, A2]);
        var overlapping = new TournamentTeam("Beta", "B", B1, [B1, A2]);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new TournamentMatchConfiguration(
                Guid.NewGuid(), TournamentBestOf.Three, teamA, overlapping));

        var machine = NewMachine(TournamentBestOf.Three);
        machine.OpenReady();
        foreach (var player in new[] { A1, A2, B1, B2 })
            machine.Ready(player);
        machine.BeginVeto();
        Assert.ThrowsExactly<ArgumentException>(() =>
            machine.CompleteVeto(["de_dust2"]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            machine.CompleteVeto(["de_dust2", "de_dust2", "de_nuke"]));
    }

    [TestMethod]
    public void Restore_RejectsImpossibleSeriesWinnerOutsideCompletedState()
    {
        var machine = NewMachine(TournamentBestOf.Three);
        var snapshot = new TournamentRecoverySnapshot(
            machine.Configuration.MatchId,
            TournamentMatchState.Live,
            ["de_dust2", "de_nuke", "de_inferno"],
            CurrentMapIndex: 2,
            TeamAMaps: 2,
            TeamBMaps: 0,
            ReadyPlayers: [],
            KnifeWinner: null,
            SideChooser: null,
            TeamASide: PlayerTeam.Terrorist,
            TeamBSide: PlayerTeam.CounterTerrorist,
            ResumeState: null);

        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentMatchStateMachine.Restore(machine.Configuration, snapshot));
    }

    [TestMethod]
    public void NonRosteredPlayers_CannotReadyAndHaveNoForcedSide()
    {
        var outsider = new PlayerId(76561198000186999);
        var machine = NewMachine(TournamentBestOf.One);
        machine.OpenReady();

        Assert.ThrowsExactly<InvalidOperationException>(() => machine.Ready(outsider));
        Assert.IsNull(machine.AssignedSide(outsider));
    }

    private static TournamentMatchStateMachine LiveMachine(TournamentBestOf bestOf)
    {
        var machine = NewMachine(bestOf);
        machine.OpenReady();
        foreach (var player in new[] { A1, A2, B1, B2 })
            machine.Ready(player);
        machine.BeginVeto();
        var maps = bestOf switch
        {
            TournamentBestOf.One => new[] { "de_dust2" },
            TournamentBestOf.Three => new[] { "de_dust2", "de_nuke", "de_inferno" },
            _ => new[] { "de_dust2", "de_nuke", "de_inferno", "de_mirage", "de_ancient" },
        };
        machine.CompleteVeto(maps);
        machine.CompleteKnife(TournamentTeamSlot.TeamA);
        machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.Terrorist);
        return machine;
    }

    private static TournamentMatchStateMachine NewMachine(TournamentBestOf bestOf)
    {
        var teamA = new TournamentTeam("Alpha", "A", A1, [A1, A2]);
        var teamB = new TournamentTeam("Beta", "B", B1, [B1, B2]);
        return new TournamentMatchStateMachine(
            new TournamentMatchConfiguration(Guid.NewGuid(), bestOf, teamA, teamB));
    }
}
