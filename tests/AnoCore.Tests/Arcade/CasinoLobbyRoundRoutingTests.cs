using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoLobbyRoundRoutingTests
{
    private static readonly PlayerId Player = new(76561198000911901);

    [TestMethod]
    public async Task AllDurableGameCardsDispatchToRoundPresenterWithoutWalletMutation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var permission = new Permission();
        var commands = new CommandRegistry(permission);
        var hud = new TestCustomHudService();
        var selected = new List<(PlayerId Player, CasinoRoundGame Game)>();
        using var presenter = new CasinoPresenter(hud, players, permission, new ReadOnlyWallet(), commands, events, _ => { },
            openRound: (player, game, _) => { selected.Add((player, game)); return ValueTask.FromResult(true); });
        await players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        Assert.IsTrue((await commands.ExecuteAsync("!anocasino", Player)).Success);
        foreach (var card in new[] { "blackjack", "risk", "book" })
            await hud.ClickAsync(Player, CasinoPresenter.LobbyHudId, "casino_lobby_" + card);
        CollectionAssert.AreEqual(new[] { CasinoRoundGame.Blackjack, CasinoRoundGame.Risk, CasinoRoundGame.Book }, selected.Select(x => x.Game).ToArray());
        Assert.IsTrue(selected.All(x => x.Player == Player));
        permission.Allowed = false;
        await hud.ClickAsync(Player, CasinoPresenter.LobbyHudId, "casino_lobby_blackjack");
        Assert.AreEqual(3, selected.Count);
        Assert.AreEqual(0, hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Count);
    }

    [TestMethod]
    public async Task FailedRoundOpenLeavesLobbyVisibleAndOffersRefresh()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var permission = new Permission();
        var commands = new CommandRegistry(permission);
        var hud = new TestCustomHudService();
        using var presenter = new CasinoPresenter(hud, players, permission, new ReadOnlyWallet(), commands, events, _ => { },
            openRound: (_, _, _) => ValueTask.FromException<bool>(new InvalidOperationException("Connection lost")));
        await players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        Assert.IsTrue((await commands.ExecuteAsync("!anocasino", Player)).Success);
        await hud.ClickAsync(Player, CasinoPresenter.LobbyHudId, "casino_lobby_book");
        Assert.AreEqual(1, hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Count);
        Assert.AreEqual("Spiel konnte nicht geladen werden. Bitte aktualisieren.", hud.Text(Player, CasinoPresenter.LobbyHudId, "casino_lobby_status"));
        await hud.ClickAsync(Player, CasinoPresenter.LobbyHudId, "casino_lobby_refresh");
        Assert.AreEqual(1, hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Count);
    }

    private sealed class Permission : IPermissionEvaluator
    {
        public bool Allowed = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId player, PermissionId permission, CancellationToken token = default)
            => ValueTask.FromResult(Allowed);
    }

    private sealed class ReadOnlyWallet : ICasinoWallet
    {
        public ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult(new CasinoBalance(2000, 1000, 0));
        public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<CasinoReceipt>>([]);
        public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation operation, CancellationToken token = default)
            => throw new AssertFailedException("Opening a game card must not debit the wallet.");
    }
}
