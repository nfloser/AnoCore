using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoConfigurationTests
{
    [TestMethod]
    public void Defaults_AreValidAndProduceRuntimeOptions()
    {
        var configuration = new AnoVetoConfiguration();

        Assert.IsTrue(configuration.Enabled);
        Assert.AreEqual(30, configuration.DurationSeconds);
        Assert.AreEqual(1, configuration.MinimumVotes);
        Assert.AreEqual(VoteTieBreakPolicy.OptionOrder, configuration.TieBreakPolicy);
        Assert.HasCount(0, AnoVetoConfiguration.Validate(configuration));

        var options = configuration.ToOptions();
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Duration);
        Assert.AreEqual(1, options.MinimumVotes);
        Assert.AreEqual(VoteTieBreakPolicy.OptionOrder, options.TieBreakPolicy);
    }

    [TestMethod]
    public void Validate_RejectsInvalidDurationMinimumVotesAndTiePolicy()
    {
        var configuration = new AnoVetoConfiguration
        {
            DurationSeconds = 0,
            MinimumVotes = 0,
            TieBreakPolicy = (VoteTieBreakPolicy)999,
        };

        var errors = AnoVetoConfiguration.Validate(configuration);

        Assert.HasCount(3, errors);
    }
}
