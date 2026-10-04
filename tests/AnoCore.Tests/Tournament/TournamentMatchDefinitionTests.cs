using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchDefinitionTests
{
    [TestMethod]
    public void DisabledDefault_IsValidButCannotActivate()
    {
        var definition = TournamentMatchDefinition.Default;

        Assert.IsFalse(definition.Enabled);
        Assert.AreEqual(0, TournamentMatchDefinition.Validate(definition).Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => definition.ToConfiguration());
    }

    [TestMethod]
    public void EnabledDefinition_ConvertsToValidatedConfiguration()
    {
        var definition = ValidDefinition();

        var configuration = definition.ToConfiguration();

        Assert.AreEqual(TournamentBestOf.Three, configuration.BestOf);
        Assert.AreEqual("Alpha", configuration.TeamA.Name);
        Assert.AreEqual("B", configuration.TeamB.Tag);
        Assert.AreEqual(2, configuration.MapsToWin);
    }

    [TestMethod]
    public void EnabledDefinition_RejectsOverlapMissingCaptainAndInvalidBestOf()
    {
        var definition = ValidDefinition();
        definition.BestOf = 2;
        definition.TeamB.Members.Add(definition.TeamA.Members[0]);
        definition.TeamA.CaptainSteamId = 76561198000197999;

        var errors = TournamentMatchDefinition.Validate(definition);

        Assert.IsTrue(errors.Any(error => error.Contains("BestOf", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("both tournament teams", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("CaptainSteamId", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() => definition.ToConfiguration());
    }

    public static TournamentMatchDefinition ValidDefinition()
        => new()
        {
            Enabled = true,
            MatchId = "22222222-2222-2222-2222-222222222222",
            BestOf = 3,
            KnifeRound = true,
            OvertimeEnabled = true,
            TeamA = new TournamentTeamDefinition
            {
                Name = "Alpha",
                Tag = "A",
                CaptainSteamId = 76561198000197001,
                Members = [76561198000197001, 76561198000197002],
            },
            TeamB = new TournamentTeamDefinition
            {
                Name = "Beta",
                Tag = "B",
                CaptainSteamId = 76561198000197011,
                Members = [76561198000197011, 76561198000197012],
            },
        };
}
