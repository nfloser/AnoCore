using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoCustomHudTests
{
    private static readonly PlayerId Manager = new(76561198000000701);
    private static readonly PlayerId PlayerA = new(76561198000000702);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Show_RendersEightClickableMapsAndCapturesInput()
    {
        var hud = new RecordingHudService();
        var coordinator = CreateCoordinator();
        var created = await coordinator.CreateAsync(Manager, [Manager, PlayerA], Now);
        Assert.IsTrue(created.Accepted);
        using var controller = new AnoVetoHudController(hud, coordinator, new FixedTimeProvider(Now));

        var shown = controller.Show(PlayerA);

        Assert.IsTrue(shown);
        Assert.IsNotNull(hud.Definition);
        Assert.AreEqual(AnoVetoHudController.LayoutResource, hud.Definition.LayoutResource);
        Assert.IsTrue(hud.Definition.CaptureInput);
        Assert.HasCount(9, hud.Definition.ButtonIds);
        Assert.IsTrue(hud.VisiblePlayers.Contains(PlayerA));
        for (var index = 0; index < 8; index++)
        {
            Assert.AreEqual($"Map {index + 1:00}", hud.Text[(PlayerA, $"ano_veto_map_{index}_text", "text")]);
        }
    }

    [TestMethod]
    public async Task MapClick_CastsVoteAndHidesHudForThatPlayer()
    {
        var hud = new RecordingHudService();
        var coordinator = CreateCoordinator();
        var created = await coordinator.CreateAsync(Manager, [Manager, PlayerA], Now);
        Assert.IsTrue(created.Accepted);
        using var controller = new AnoVetoHudController(hud, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue(controller.Show(PlayerA));

        await hud.ClickAsync(PlayerA, "ano_veto_map_3");

        Assert.IsFalse(hud.VisiblePlayers.Contains(PlayerA));
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        var secondVote = await coordinator.CastAsync(PlayerA, maps[4].MapId, Now.AddSeconds(1));
        Assert.IsFalse(secondVote.Accepted);
        Assert.AreEqual(AnoVetoFailure.AlreadyVoted, secondVote.Failure);
    }

    [TestMethod]
    public async Task CloseClick_HidesHudWithoutCastingVote()
    {
        var hud = new RecordingHudService();
        var coordinator = CreateCoordinator();
        var created = await coordinator.CreateAsync(Manager, [Manager, PlayerA], Now);
        Assert.IsTrue(created.Accepted);
        using var controller = new AnoVetoHudController(hud, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue(controller.Show(PlayerA));

        await hud.ClickAsync(PlayerA, "ano_veto_close");

        Assert.IsFalse(hud.VisiblePlayers.Contains(PlayerA));
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        var firstVote = await coordinator.CastAsync(PlayerA, maps[0].MapId, Now.AddSeconds(1));
        Assert.IsTrue(firstVote.Accepted);
    }

    [TestMethod]
    public async Task FinalVote_HidesHudForEveryone()
    {
        var hud = new RecordingHudService();
        var coordinator = CreateCoordinator(minimumVotes: 2);
        var created = await coordinator.CreateAsync(Manager, [Manager, PlayerA], Now);
        Assert.IsTrue(created.Accepted);
        using var controller = new AnoVetoHudController(hud, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue(controller.Show(Manager));
        Assert.IsTrue(controller.Show(PlayerA));
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.IsTrue((await coordinator.CastAsync(Manager, maps[0].MapId, Now.AddSeconds(1))).Accepted);

        await hud.ClickAsync(PlayerA, "ano_veto_map_0");

        Assert.IsEmpty(hud.VisiblePlayers);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
    }

    private static AnoVetoCoordinator CreateCoordinator(int minimumVotes = 1)
    {
        var permissions = new AllowManagerPermissions();
        return new AnoVetoCoordinator(
            new MapCatalog(Enumerable.Range(1, 8)
                .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}"))),
            new VoteService(permissions),
            new NoOpMapChanger(),
            new StableRandomSource(),
            new AnoVetoOptions(TimeSpan.FromSeconds(30), minimumVotes, VoteTieBreakPolicy.OptionOrder));
    }

    private sealed class RecordingHudService : ICustomHudService
    {
        private CustomHudClickHandler? _handler;

        public CustomHudDefinition? Definition { get; private set; }
        public HashSet<PlayerId> VisiblePlayers { get; } = [];
        public Dictionary<(PlayerId Player, string Panel, string Variable), string> Text { get; } = [];

        public IDisposable Register(ModuleId owner, CustomHudDefinition definition, CustomHudClickHandler? clickHandler = null)
        {
            Definition = definition;
            _handler = clickHandler;
            return new Registration(() =>
            {
                Definition = null;
                _handler = null;
                VisiblePlayers.Clear();
            });
        }

        public bool Show(PlayerId playerId, CustomHudId hudId)
        {
            if (Definition?.Id != hudId)
            {
                return false;
            }

            VisiblePlayers.Add(playerId);
            return true;
        }

        public bool Hide(PlayerId playerId, CustomHudId hudId)
        {
            if (Definition?.Id != hudId)
            {
                return false;
            }

            VisiblePlayers.Remove(playerId);
            return true;
        }

        public void HideAll(CustomHudId hudId)
        {
            if (Definition?.Id == hudId)
            {
                VisiblePlayers.Clear();
            }
        }

        public bool SetText(PlayerId playerId, CustomHudId hudId, string panelId, string value, string variableName = "text")
        {
            if (Definition?.Id != hudId)
            {
                return false;
            }

            Text[(playerId, panelId, variableName)] = value;
            return true;
        }

        public bool SetClass(PlayerId playerId, CustomHudId hudId, string panelId, string className, bool enabled)
            => Definition?.Id == hudId;

        public async ValueTask ClickAsync(PlayerId playerId, string buttonId)
        {
            Assert.IsNotNull(Definition);
            Assert.IsNotNull(_handler);
            await _handler(new CustomHudClickContext(playerId, Definition.Id, buttonId, CancellationToken.None));
        }

        private sealed class Registration(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed class AllowManagerPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
    }

    private sealed class NoOpMapChanger : IMapChanger
    {
        public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class StableRandomSource : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count) => source.Take(count).ToArray();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
