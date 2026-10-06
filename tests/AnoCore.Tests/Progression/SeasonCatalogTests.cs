using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class SeasonCatalogTests
{
    private static readonly DateTimeOffset January =
        new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February =
        new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset March =
        new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ResolveAt_UsesHalfOpenBoundariesAndAdjacentSeasons()
    {
        var catalog = SeasonCatalogSnapshot.Create(
        [
            new("s1", 1, "Season One", January, February),
            new("s2", 1, "Season Two", February, March),
        ]);

        var first = catalog.ResolveAt(January);
        Assert.AreEqual("s1", first.Current?.Id);
        Assert.IsNull(first.Previous);
        Assert.AreEqual("s2", first.Next?.Id);

        var boundary = catalog.ResolveAt(February);
        Assert.AreEqual("s2", boundary.Current?.Id);
        Assert.AreEqual("s1", boundary.Previous?.Id);
        Assert.IsNull(boundary.Next);

        var after = catalog.ResolveAt(March);
        Assert.IsNull(after.Current);
        Assert.AreEqual("s2", after.Previous?.Id);
        Assert.IsNull(after.Next);
    }

    [TestMethod]
    public void ResolveAt_GapReturnsPreviousAndNext()
    {
        var catalog = SeasonCatalogSnapshot.Create(
        [
            new("s1", 1, "Season One", January, January.AddDays(7)),
            new("s2", 1, "Season Two", February, March),
        ]);

        var gap = catalog.ResolveAt(January.AddDays(14));

        Assert.IsNull(gap.Current);
        Assert.AreEqual("s1", gap.Previous?.Id);
        Assert.AreEqual("s2", gap.Next?.Id);
    }

    [TestMethod]
    public void Snapshot_IsOrderedAndIsolatedFromCallerMutation()
    {
        var source = new List<SeasonDefinition>
        {
            new("s2", 2, "Season Two", February, March),
            new("s1", 4, "Season One", January, February),
        };

        var catalog = SeasonCatalogSnapshot.Create(source);
        source.Clear();

        Assert.AreEqual(2, catalog.Seasons.Count);
        Assert.AreEqual("s1", catalog.Seasons[0].Id);
        Assert.AreEqual("s2", catalog.Seasons[1].Id);
    }

    [TestMethod]
    public void Create_RejectsInvalidOrOverlappingDefinitions()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
                [new("", 1, "Season", January, February)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
                [new("s1", 0, "Season", January, February)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
                [new("s1", 1, "", January, February)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
                [new("s1", 1, "Season",
                    January.ToOffset(TimeSpan.FromHours(1)), February)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
                [new("s1", 1, "Season", January, January)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SeasonCatalogSnapshot.Create(
            [
                new("same", 1, "A", January, February),
                new("same", 2, "B", March, March.AddMonths(1)),
            ]));
        Assert.ThrowsExactly<SeasonOverlapException>(() =>
            SeasonCatalogSnapshot.Create(
            [
                new("s1", 1, "A", January, February),
                new("s2", 1, "B", January.AddDays(10), March),
            ]));
    }
}
