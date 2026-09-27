using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Placeholders;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankPlaceholderTests
{
    private static readonly PlayerId Player = new(76561198000012611);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-rank-placeholder-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task Placeholders_ResolveConfiguredTagNameAndAdjustedPoints()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("ranks", new RankConfiguration
        {
            KillPoints = 3,
            AssistPoints = 2,
            DeathPenalty = 1,
            Thresholds =
            [
                new RankThreshold("Recruit", 0, "[R]"),
                new RankThreshold("Veteran", 10, "[V]"),
            ],
        });
        var placeholders = new PlaceholderRegistry();
        var repository = new FakeRepository(
            new CombatScoreRankEntry(Player, 10, 4));
        using var module = await RankModule.CreateAsync(
            store, Commands(), Players(), repository, placeholders);

        var context = new PlaceholderContext(
            new Dictionary<string, object?> { ["player"] = Player });
        var value = await placeholders.ResolveAsync(
            "{rank.tag}|{rank.name}|{rank.points}", context);

        Assert.AreEqual("[V]|Veteran|10", value);
        Assert.AreEqual((3, 2, 1), repository.LastWeights);
    }

    [TestMethod]
    public async Task Placeholders_MissingPlayerResolveEmptyWithoutStorageRead()
    {
        var placeholders = new PlaceholderRegistry();
        var repository = new FakeRepository(
            new CombatScoreRankEntry(Player, 10, 4));
        using var module = await RankModule.CreateAsync(
            new JsonConfigStore(_root), Commands(), Players(), repository, placeholders);

        var value = await placeholders.ResolveAsync(
            "x{rank.tag}{rank.name}{rank.points}y", PlaceholderContext.Empty);

        Assert.AreEqual("xy", value);
        Assert.AreEqual(0, repository.ScoreReads);
    }

    [TestMethod]
    public async Task Creation_RollsBackPlaceholdersAndCommandsOnCollision()
    {
        var placeholders = new PlaceholderRegistry();
        using var occupied = placeholders.Register(
            new ModuleId("test"), "rank.points", (_, _) => ValueTask.FromResult<string?>("x"));
        var commands = Commands();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await RankModule.CreateAsync(new JsonConfigStore(_root), commands, Players(),
                new FakeRepository(null), placeholders));

        Assert.IsFalse(placeholders.Contains("rank.tag"));
        Assert.IsFalse(placeholders.Contains("rank.name"));
        Assert.IsTrue(placeholders.Contains("rank.points"));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorank", Player)).FailureReason);
    }

    [TestMethod]
    public async Task Disposal_RemovesOwnedPlaceholders()
    {
        var placeholders = new PlaceholderRegistry();
        var module = await RankModule.CreateAsync(
            new JsonConfigStore(_root), Commands(), Players(),
            new FakeRepository(null), placeholders);

        module.Dispose();

        Assert.IsFalse(placeholders.Contains("rank.tag"));
        Assert.IsFalse(placeholders.Contains("rank.name"));
        Assert.IsFalse(placeholders.Contains("rank.points"));
    }

    [TestMethod]
    public void Configuration_RejectsInvalidTags()
    {
        var configuration = RankConfiguration.Default;
        configuration.Thresholds =
        [
            new RankThreshold("Recruit", 0, "line\nbreak"),
            new RankThreshold("Veteran", 10, new string('x', 25)),
        ];

        var errors = RankConfiguration.Validate(configuration);

        Assert.IsTrue(errors.Any(error => error.Contains("tags", StringComparison.OrdinalIgnoreCase)));
    }

    private static CommandRegistry Commands() => new(new AllowAll());

    private static PlayerRegistry Players() => new(new AnoEventBus());

    private sealed class AllowAll : AnoCore.Abstractions.Permissions.IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id,
            AnoCore.Abstractions.Permissions.PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FakeRepository(CombatScoreRankEntry? placement) : ICombatRepository
    {
        public int ScoreReads { get; private set; }
        public (int Kill, int Assist, int Death)? LastWeights { get; private set; }

        public ValueTask RecordAsync(CombatDeath death,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<CombatTotals> ReadAsync(PlayerId id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CombatTotals(0, 0, 0));

        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(
            PlayerId playerId, int killPoints, int assistPoints, int deathPenalty,
            CancellationToken cancellationToken = default)
        {
            ScoreReads++;
            LastWeights = (killPoints, assistPoints, deathPenalty);
            return ValueTask.FromResult(placement);
        }

        public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
            int killPoints, int assistPoints, int deathPenalty, int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatScoreRankEntry>>([]);
    }
}
