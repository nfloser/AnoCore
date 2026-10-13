using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentPresetTests
{
    [TestMethod]
    public void EveryPresetSupportsThreeToTenRealRostersAndPredictsFixtureBounds()
    {
        for (var count = 3; count <= 10; count++)
            foreach (var preset in Enum.GetValues<TournamentEventPreset>())
            {
                var definition = Definition(count);
                var configured = TournamentEventPresets.Apply(definition, preset);
                Assert.IsEmpty(TournamentEventDefinition.Validate(configured));
                CollectionAssert.AreEqual(definition.Teams.SelectMany(team => team.Starters).ToArray(), configured.Teams.SelectMany(team => team.Starters).ToArray());
                var preview = TournamentEventPresets.Preview(configured);
                Assert.AreEqual(count, preview.Teams);
                Assert.IsTrue(preview.MinimumMatches > 0);
                Assert.IsTrue(preview.MaximumMatches >= preview.MinimumMatches);
                Assert.AreEqual(count, definition.Teams.Count);
                Assert.AreEqual(0, definition.Groups.Count);
            }
    }
    [TestMethod]
    public void PresetClonesSourceAndVersionedImportsRejectUnknownSchema()
    {
        var source = Definition(5);
        var result = TournamentEventPresets.Apply(source, TournamentEventPreset.Groups);
        result.Teams[0].Starters[0] = 1;
        Assert.AreNotEqual(1UL, source.Teams[0].Starters[0]);
        Assert.IsNotEmpty(TournamentEventImportConfiguration.Validate(new() { SchemaVersion = 99, Enabled = true, Event = source }));
        var preview = TournamentEventPresets.Preview(TournamentEventPresets.Apply(source, TournamentEventPreset.DoubleElimination));
        Assert.AreEqual(8, preview.MinimumMatches);
        Assert.AreEqual(9, preview.MaximumMatches);
    }
    private static TournamentEventDefinition Definition(int count) => new()
    {
        Id = "preset",
        Name = "Preset",
        Teams = Enumerable.Range(1, count).Select(i => new TournamentEventTeamDefinition
        {
            Id = $"team-{i}",
            Name = $"Team {i}",
            Tag = $"T{i}",
            InitialSeed = i,
            CaptainSteamId = (ulong)(1000 + i * 10),
            Starters = Enumerable.Range(0, 5).Select(j => (ulong)(1000 + i * 10 + j)).ToList()
        }).ToList(),
    };
}
