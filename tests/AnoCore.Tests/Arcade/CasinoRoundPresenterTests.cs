using System.Collections.Immutable;
using System.Text.Json;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoRoundPresenterTests
{
    private static readonly PlayerId Player = new(76561198000911901);

    [TestMethod]
    [DataRow(CasinoRoundGame.BiggestBoy)]
    [DataRow(CasinoRoundGame.Book)]
    [DataRow(CasinoRoundGame.Blackjack)]
    [DataRow(CasinoRoundGame.Risk)]
    public async Task RoundBalancesAreCompactAndStakeRemainsExact(CasinoRoundGame game)
    {
        using var f = await Fixture.Create();
        f.Wallet.Balance = new(75_000_000, 1_250_000_000, 0);
        await f.Presenter.OpenAsync(Player, game);
        Assert.AreEqual("75 Mio. €", f.Text("account"));
        Assert.AreEqual("1,25 Mrd. €", f.Text("bankroll"));
        Assert.AreEqual("100 €", f.Text("stake"));
    }

    [TestMethod]
    public async Task BiggestBoyWelcomeGridAppearsOnOpenAndIdleRefresh()
    {
        using var f = await Fixture.Create();
        int[] expected = [10, 0, 2, 10, 6, 5, 11, 7, 1, 10, 8, 4, 10, 9, 3];
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        for (var cell = 0; cell < expected.Length; cell++)
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + expected[cell]), $"Opening cell {cell}");
        await f.Click("refresh");
        for (var cell = 0; cell < expected.Length; cell++)
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + expected[cell]), $"Refresh cell {cell}");
        Assert.IsTrue(f.Class("balanced-mode"));
        Assert.AreEqual(0, f.Rounds.Calls.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WelcomeGridDoesNotReplaceBookedWinOrResumedBonus(bool bonus)
    {
        using var f = await Fixture.Create();
        var spin = ControlledSlotRules.Biggest(100, new Random(518).Next,
            bonus ? new(0, 100, 0) : new(100, 0, 100));
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, !bonus,
            100, spin.Payout, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        await f.Click("play");
        f.FinishAnimation();
        for (var cell = 0; cell < 15; cell++)
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + (int)spin.Grid[cell]));
        if (bonus)
        {
            await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
            await f.Click("refresh");
            for (var cell = 0; cell < 15; cell++)
                Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + (int)spin.Grid[cell]));
        }
        else
        {
            await f.Click("refresh");
            Assert.IsTrue(f.PanelClass("bb_symbol0", "bb-symbol-10"));
            Assert.IsTrue(f.PanelClass("bb_symbol14", "bb-symbol-3"));
        }
        Assert.AreEqual(1, f.Rounds.Calls.Count);
    }

    [TestMethod]
    public async Task BiggestBoyStakeControlsReachOneHundredThousand()
    {
        using var f = await Fixture.Create(biggestMaximum: 100000);
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        for (var i = 0; i < 15; i++) await f.Click("plus");
        Assert.AreEqual("100.000 €", f.Text("stake"));
        await f.Click("minus");
        Assert.AreEqual("50.000 €", f.Text("stake"));
    }

    [TestMethod]
    public async Task FruitStakeControlsReachConfiguredHighLimitInPracticalSteps()
    {
        using var f = await Fixture.Create(new() { Book = 100000 });
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Book);
        await f.Click("plus");
        Assert.AreEqual("250 €", f.Text("stake"));
        for (var i = 0; i < 15; i++) await f.Click("plus");
        Assert.AreEqual("100.000 €", f.Text("stake"));
        await f.Click("minus");
        Assert.AreEqual("50.000 €", f.Text("stake"));
    }

    [TestMethod]
    public async Task ControlledBiggestBoyScrollsAllSymbolsBeforeLandingOnBookedFullScreen()
    {
        using var f = await Fixture.Create();
        var spin = ControlledSlotRules.Biggest(100, _ => 0, new(100, 0, 100));
        Assert.AreEqual(1, spin.RawGrid.Distinct().Count());
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, true,
            100, spin.Payout, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        await f.Click("play");
        var seen = new HashSet<int>();
        for (var tick = 0; tick < 37; tick++)
        {
            f.Clock.AdvanceMilliseconds(50); f.Presenter.Tick();
            for (var row = 0; row < 4; row++)
                for (var symbol = 0; symbol < 12; symbol++)
                    if (f.PanelClass($"bb_motion0_{row}", "bb-symbol-" + symbol)) seen.Add(symbol);
        }
        Assert.AreEqual(12, seen.Count, "A full-screen result must not restrict the spinning ribbon to the booked symbol.");
        Assert.IsFalse(f.PanelClass("bb_reel0", "stopped"));
        Assert.AreEqual("–", f.Text("payout"));
        f.FinishAnimation();
        for (var cell = 0; cell < 15; cell++)
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + (int)spin.Grid[cell]));
        Assert.AreEqual(CasinoMoney.Format(spin.Payout), f.Text("payout"));
        Assert.AreEqual(1, f.Rounds.Calls.Count);
    }

    [TestMethod]
    public async Task SoundToggleIsPerSessionAndDoesNotBookOrReplayStops()
    {
        using var f = await Fixture.Create();
        var spin = ControlledSlotRules.Biggest(100, new Random(509).Next, new(100, 0, 100));
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, true,
            100, spin.Payout, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        Assert.AreEqual("WALZENTON: AN", f.Text("sound_text"));
        await f.Click("sound");
        Assert.AreEqual("WALZENTON: AUS", f.Text("sound_text"));
        Assert.AreEqual(0, f.Rounds.Calls.Count);
        await f.Click("play");
        f.FinishAnimation();
        Assert.AreEqual(0, f.ReelClicks.Count);
        Assert.IsTrue(f.Text("detail")!.Contains("VOLLBILD!", StringComparison.Ordinal));
        await f.Click("sound");
        await f.Click("play");
        f.FinishAnimation();
        Assert.AreEqual(5, f.ReelClicks.Count);
        f.Presenter.Tick();
        Assert.AreEqual(5, f.ReelClicks.Count);
    }

    [TestMethod]
    [DataRow(CasinoRoundGame.BiggestBoy)]
    [DataRow(CasinoRoundGame.Book)]
    public async Task StakeRemainsLockedDuringBonusAndUnlocksAfterFinalSpin(CasinoRoundGame game)
    {
        using var f = await Fixture.Create();
        var rng = new Random(7);
        var json = game == CasinoRoundGame.BiggestBoy
            ? JsonSerializer.Serialize(ControlledSlotRules.Biggest(100, rng.Next, new(0, 100, 0)))
            : JsonSerializer.Serialize(ControlledSlotRules.Book(100, rng.Next, new(0, 100, 0)));
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, game, 100, 1, false, 100, 0, json);
        await f.Presenter.OpenAsync(Player, game);
        await f.Click("play");
        f.FinishAnimation();
        await f.Click("plus");
        Assert.AreEqual("100 €", f.Text("stake"));
        f.Rounds.NextSnapshot = f.Rounds.Current! with { Revision = 2, IsComplete = true };
        await f.Click("free");
        f.FinishAnimation();
        await f.Click("plus");
        Assert.AreEqual("250 €", f.Text("stake"));
    }

    [TestMethod]
    public async Task RoundControlsUseEachConfiguredMaximum()
    {
        using var f = await Fixture.Create(new() { Blackjack = 1000, Risk = 50 });
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack);
        for (var n = 0; n < 25; n++) await f.Click("plus");
        Assert.AreEqual("1.000 €", f.Text("stake"));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        Assert.AreEqual("50 €", f.Text("stake"));
        await f.Click("plus");
        Assert.AreEqual("50 €", f.Text("stake"));
    }

    [TestMethod]
    [DataRow(CasinoRoundGame.Book)]
    [DataRow(CasinoRoundGame.BiggestBoy)]
    public async Task ReelSpinArmsBeforeStartingOnALaterUiUpdate(CasinoRoundGame game)
    {
        using var f = await Fixture.Create();
        var json = game == CasinoRoundGame.Book
            ? JsonSerializer.Serialize(BonusSlotRules.StartRound(100, _ => 0))
            : JsonSerializer.Serialize(BiggestBoyCollectorRules.StartRound(100, _ => 0));
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, game, 100, 1, false, 100, 0, json);
        await f.Presenter.OpenAsync(Player, game);
        await f.Click("play");
        Assert.IsTrue(f.Class("reels-armed"));
        Assert.IsFalse(f.Class("motion-a"));
        f.Clock.AdvanceMilliseconds(150); f.Presenter.Tick();
        Assert.IsTrue(f.Class("reels-armed"));
        Assert.IsFalse(f.Class("motion-a"));
        f.Clock.AdvanceMilliseconds(100); f.Presenter.Tick();
        Assert.IsFalse(f.Class("reels-armed"));
        Assert.IsTrue(f.Class("motion-a"));
        await f.Click("free");
        Assert.AreEqual(1, f.Rounds.Calls.Count);
        f.FinishAnimation();
        Assert.IsFalse(f.Class("motion-a"));
        Assert.IsFalse(f.Class("reels-armed"));
    }

    [TestMethod]
    public async Task BiggestBoyProjectsAllCellsAndHidesWinningLinesUntilFiveLapsFinish()
    {
        using var f = await Fixture.Create();
        var trigger = BiggestBoyRules.StartRound(100, _ => 0);
        var selected = BiggestBoyRules.SelectExpandingSymbol(trigger, BiggestBoySymbol.Beer);
        var stop = Enumerable.Range(0, 100)
            .First(value => BiggestBoyRules.DrawGrid(_ => value).Contains(BiggestBoySymbol.Beer));
        var spin = BiggestBoyRules.PlayFreeSpin(selected.State, _ => stop);
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, false,
            100, spin.State.TotalPayout, JsonSerializer.Serialize(spin));
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy));
        await f.Click("play");
        Assert.AreEqual("WALZEN LAUFEN …", f.Text("biggest_win"));
        Assert.AreEqual("", f.Text("detail"));
        for (var cell = 0; cell < 25; cell++)
        {
            Assert.IsTrue(f.PanelClass("biggest_symbol" + cell, "biggest-symbol-" + (int)spin.Grid[cell]));
            Assert.IsFalse(f.PanelClass("biggest_symbol" + cell, "winning"));
            Assert.IsTrue(f.PanelClass("biggest_repeat" + cell, "biggest-symbol-" + (int)spin.Grid[cell]));
        }
        await f.Click("free");
        Assert.AreEqual(1, f.Rounds.Calls.Count);
        f.Clock.AdvanceMilliseconds(1950); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("biggest_reel0", "settling"));
        Assert.IsFalse(f.PanelClass("biggest_reel1", "settling"));
        f.Clock.AdvanceMilliseconds(450); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("biggest_reel0", "stopped"));
        Assert.IsFalse(f.PanelClass("biggest_reel0", "settling"));
        f.Clock.AdvanceMilliseconds(1100); f.Presenter.Tick();
        Assert.IsTrue(f.Text("biggest_win")!.StartsWith(spin.Payout > 0 ? "GEWINN DIESES SPINS: " : "KEIN GEWINN", StringComparison.Ordinal));
        Assert.AreEqual("SPINGEWINN: " + CasinoMoney.Format(spin.Payout), f.Text("biggest_spin_win"));
        Assert.IsTrue(spin.ExpandedReels.Length > 0);
        Assert.IsTrue(spin.ExpandedReels.Any(reel => f.PanelClass("biggest_symbol" + reel * 5, "expanded")));
        await f.Click("free");
        Assert.AreEqual(2, f.Rounds.Calls.Count);
        Assert.IsTrue(f.Class("reels-armed"));
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.IsTrue(f.Class("motion-b"));
        Assert.IsFalse(f.PanelClass("biggest_reel0", "stopped"));
        Assert.IsFalse(f.PanelClass("biggest_reel0", "settling"));
    }

    [TestMethod]
    public async Task CollectorProjectsThreeRowsAndMoneyAndNeedsNoSymbolPicker()
    {
        using var f = await Fixture.Create();
        var trigger = BiggestBoyCollectorRules.StartRound(100, _ => 0);
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(trigger.State, _ => 5);
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, false,
            100, spin.State.TotalPayout, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        await f.Click("play");
        Assert.IsTrue(f.Class("collector-mode"));
        Assert.IsFalse(f.Class("needs-bonus-symbol"));
        for (var reel = 0; reel < 5; reel++)
            for (var row = 0; row < 3; row++)
            {
                var cell = reel * 3 + row;
                var display = reel * 5 + row;
                Assert.IsTrue(f.PanelClass("biggest_symbol" + display, "biggest-symbol-" + (int)spin.Grid[cell]));
                Assert.IsTrue(f.PanelClass("biggest_repeat" + display, "biggest-symbol-" + (int)spin.Grid[cell]));
                Assert.AreEqual(spin.MoneyValues[cell] > 0 ? CasinoMoney.Format(spin.MoneyValues[cell]) : "", f.Text("biggest_symbol_money" + display));
            }
        f.FinishAnimation();
        Assert.IsTrue(f.Text("biggest_bonus")!.Contains("×2", StringComparison.Ordinal));
        Assert.IsTrue(f.Text("detail")!.Contains("Spingewinn:", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExpandingUsesItsOwnThreeRowHudAndShowsSeparateSpinAndBonusValues()
    {
        using var f = await Fixture.Create();
        var trigger = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        var selected = trigger.State.ExpandingSymbol!.Value;
        var stops = Enumerable.Range(0, 5).Select(reel => Enumerable.Range(0, 100).First(stop =>
            Enumerable.Range(0, 3).Any(row => BiggestBoyExpandingRules.ReelStrips[reel][(stop + row) % 100] == selected)));
        var queue = new Queue<int>(stops);
        var spin = BiggestBoyExpandingRules.PlayFreeSpin(trigger.State, _ => queue.Dequeue());
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, false,
            100, spin.State.TotalPayout, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        Assert.AreEqual(CasinoRoundPresenter.ExpandingHudId, f.ActiveHud);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        await f.Click("play");
        Assert.IsTrue(f.Class("reels-armed"));
        Assert.IsFalse(f.Class("book-mode"));
        Assert.AreEqual("–", f.Text("payout"));
        Assert.AreEqual("", f.Text("detail"), "Rolling must not overwrite payout/details with status text.");
        Assert.AreEqual("", f.Text("status"));
        Assert.AreEqual(f.Text("bankroll"), f.Text("bankroll_footer"));
        for (var reel = 0; reel < 5; reel++)
            for (var row = 0; row < 4; row++)
                Assert.IsTrue(f.PanelClass($"bb_motion{reel}_{row}", "bb-symbol-"
                    + (int)BiggestBoyExpandingRules.ReelStrips[reel][row]));
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.IsTrue(f.Class("motion-a"));
        for (var reel = 0; reel < 5; reel++)
        {
            var travelled = BiggestBoyReelMotion.Travelled(0, spin.Stops[reel], .05, reel);
            for (var row = 0; row < 4; row++)
                Assert.IsTrue(f.PanelClass($"bb_motion{reel}_{row}", "bb-symbol-"
                    + (int)BiggestBoyExpandingRules.ReelStrips[reel][BiggestBoyReelMotion.VisibleIndex(0, travelled, row)]));
        }
        f.FinishAnimation();
        Assert.AreEqual(CasinoMoney.Format(spin.Payout), f.Text("payout"));
        Assert.IsTrue(f.Text("biggest_bonus")!.Contains("Expanding Symbol:", StringComparison.Ordinal));
        Assert.IsTrue(f.Text("biggest_bonus")!.Contains(CasinoMoney.Format(spin.State.BonusPayout), StringComparison.Ordinal));
        for (var cell = 0; cell < 15; cell++)
        {
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + (int)spin.Grid[cell]));
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "expanded"));
        }
        var calls = f.Rounds.Calls.Count;
        f.Clock.AdvanceMilliseconds(1250); f.Presenter.Tick();
        Assert.AreEqual(calls, f.Rounds.Calls.Count, "Cycling winning lines must not replay a money action.");
        f.Rounds.Current = f.Rounds.Current! with { IsComplete = true };
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Book);
        Assert.AreEqual(CasinoRoundPresenter.ExpandingHudId, f.ActiveHud);
        Assert.IsTrue(f.Class("book-mode"));
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy);
        Assert.AreEqual(CasinoRoundPresenter.ExpandingHudId, f.ActiveHud);
        Assert.IsFalse(f.Class("book-mode"), "Book fruit skins must be cleared before returning to Biggest Boy.");
    }

    [TestMethod]
    public async Task BiggestBoyBonusPickerBooksExactlyOnePersistentSymbolChoice()
    {
        using var f = await Fixture.Create();
        var trigger = BiggestBoyRules.StartRound(100, _ => 0);
        f.Rounds.Current = new(Guid.NewGuid(), Player, CasinoRoundGame.BiggestBoy, 100, 1, false,
            100, trigger.State.TotalPayout, JsonSerializer.Serialize(trigger));
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.BiggestBoy));
        Assert.IsTrue(f.Class("needs-bonus-symbol"));

        var selected = BiggestBoyRules.SelectExpandingSymbol(trigger, BiggestBoySymbol.Beer);
        f.Rounds.NextSnapshot = f.Rounds.Current with
        {
            Revision = 2,
            StateJson = JsonSerializer.Serialize(selected),
        };
        await f.Click("bonus1");

        Assert.AreEqual(CasinoRoundAction.SelectSymbol, f.Rounds.Last!.Action);
        Assert.AreEqual((int)BiggestBoySymbol.Beer, f.Rounds.Last.Choice);
        Assert.AreEqual(100, f.Rounds.Last.Stake);
        Assert.IsFalse(f.Class("needs-bonus-symbol"));
        Assert.IsFalse(f.Class("motion-a"));
        Assert.IsFalse(f.Class("motion-b"));
        Assert.AreEqual("Bonussymbol gewählt · Freispiele bereit", f.Text("status"));
    }

    [TestMethod]
    public async Task BlackjackCardsConcealDealerHoleAndClearAbsentHandsOnRefresh()
    {
        using var f = await Fixture.Create();
        var state = new BlackjackState(BlackjackRules.RulesVersion, 1, 100,
            [
                new([new(BlackjackSuit.Hearts, BlackjackRank.Ten), new(BlackjackSuit.Clubs, BlackjackRank.Seven)], 100, true, false, false),
                new([new(BlackjackSuit.Spades, BlackjackRank.Eight), new(BlackjackSuit.Diamonds, BlackjackRank.Eight)], 100, false, false, true)
            ],
            [new(BlackjackSuit.Clubs, BlackjackRank.Five), new(BlackjackSuit.Hearts, BlackjackRank.King)], [], 1, BlackjackRoundStatus.PlayerTurn, 0);
        var dealt = state.PlayerHands.SelectMany(hand => hand.Cards).Concat(state.DealerCards);
        state = state with
        {
            RemainingDeck = Enum.GetValues<BlackjackSuit>()
                .SelectMany(suit => Enum.GetValues<BlackjackRank>().Select(rank => new BlackjackCard(suit, rank)))
                .Except(dealt).ToImmutableArray()
        };
        f.Rounds.Current = new(Guid.NewGuid(), Player, CasinoRoundGame.Blackjack, 100, 1, false, 200, 0, JsonSerializer.Serialize(state));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack);
        Assert.AreEqual("5\n♣", f.Text("dealer_card0"));
        Assert.AreEqual("?", f.Text("dealer_card1"));
        Assert.IsTrue(f.PanelClass("dealer_card1", "face-down"));
        Assert.IsFalse(f.PanelClass("dealer_card1", "red"), "The concealed suit must not leak.");
        Assert.IsFalse(f.PanelClass("dealer_card2", "present"));
        Assert.AreEqual("10\n♥", f.Text("hand0_card0"));
        Assert.IsTrue(f.PanelClass("hand0_card0", "red"));
        Assert.IsTrue(f.PanelClass("hand1", "active-hand"));
        Assert.IsFalse(f.PanelClass("hand0", "active-hand"));
        Assert.IsTrue(f.Text("hand1_summary")!.Contains("16"));

        state = state with
        {
            RemainingDeck = state.RemainingDeck.AddRange(state.PlayerHands[1].Cards),
            PlayerHands = [state.PlayerHands[0]],
            ActiveHandIndex = 0,
            Status = BlackjackRoundStatus.Settled
        };
        f.Rounds.Current = f.Rounds.Current with { StateJson = JsonSerializer.Serialize(state) };
        await f.Click("refresh");
        Assert.AreEqual("K\n♥", f.Text("dealer_card1"));
        Assert.IsFalse(f.PanelClass("dealer_card1", "face-down"));
        Assert.IsFalse(f.PanelClass("hand1", "present"));
        Assert.IsFalse(f.PanelClass("hand1_card0", "present"));
        Assert.AreEqual("", f.Text("hand1_card0"));
        Assert.AreEqual(0, f.Rounds.Calls.Count);
    }

    [TestMethod]
    [DataRow(CasinoRoundGame.Book)]
    [DataRow(CasinoRoundGame.BiggestBoy)]
    public async Task ReelStopSoundIsPrivateExactlyOncePerReelAndResetsOnNextSpin(CasinoRoundGame game)
    {
        using var f = await Fixture.Create();
        var result = game == CasinoRoundGame.Book
            ? JsonSerializer.Serialize(BonusSlotRules.StartRound(100, _ => 0))
            : JsonSerializer.Serialize(BiggestBoyExpandingRules.StartRound(100, _ => 0));
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, game, 100, 1, false, 100, 0, result);
        await f.Presenter.OpenAsync(Player, game);
        await f.Click("play");
        f.Clock.AdvanceMilliseconds(1900); f.Presenter.Tick();
        Assert.AreEqual(0, f.ReelClicks.Count, "No click before the first visual reel stops.");
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.AreEqual(1, f.ReelClicks.Count);
        f.Presenter.Tick();
        Assert.AreEqual(1, f.ReelClicks.Count, "Tick must not repeat the stop click.");
        f.Clock.AdvanceMilliseconds(450); f.Presenter.Tick();
        Assert.AreEqual(2, f.ReelClicks.Count);
        f.FinishAnimation();
        Assert.AreEqual(5, f.ReelClicks.Count);
        Assert.IsTrue(f.ReelClicks.All(p => p == Player));
        if (game == CasinoRoundGame.Book)
        {
            f.Rounds.Current = f.Rounds.Current! with { IsComplete = true };
            await f.Presenter.OpenAsync(Player, CasinoRoundGame.Book);
            await f.Click("play");
        }
        else await f.Click("free");
        f.Clock.AdvanceMilliseconds(2150); f.Presenter.Tick();
        Assert.AreEqual(6, f.ReelClicks.Count, "A second spin must reset the sound latch.");
    }

    [TestMethod]
    public async Task BookSharesCabinet_StopsInVisible450MsOrder_AndPreservesBookedGrid()
    {
        using var f = await Fixture.Create();
        var spin = BonusSlotRules.StartRound(100, bound => bound - 1);
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.Book, 100, 1, false, 100, 0, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Book);
        Assert.AreEqual(CasinoRoundPresenter.ExpandingHudId, f.ActiveHud);
        Assert.IsTrue(f.Class("book-mode"));
        Assert.AreEqual("BOOK · FREISPIELE · 5×3", f.Text("title"));
        await f.Click("play");
        Assert.IsTrue(f.Class("reels-armed"));
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.IsFalse(f.Class("reels-armed"));
        Assert.IsTrue(f.Class("motion-a"));
        for (var reel = 0; reel < 5; reel++)
            for (var row = 0; row < 4; row++)
                Assert.IsTrue(f.PanelClass($"bb_motion{reel}_{row}", "bb-symbol-"
                    + (int)BookReelMotion.VisibleSymbol(reel, BookReelMotion.Travelled(.05, reel), row)));
        f.Clock.AdvanceMilliseconds(1850); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("bb_reel0", "stopped"));
        for (var reel = 1; reel < 5; reel++)
            Assert.IsFalse(f.PanelClass("bb_reel" + reel, "stopped"));
        f.Clock.AdvanceMilliseconds(450); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("bb_reel1", "stopped"));
        Assert.IsFalse(f.PanelClass("bb_reel2", "stopped"));
        f.Clock.AdvanceMilliseconds(450); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("bb_reel2", "stopped"));
        Assert.IsFalse(f.PanelClass("bb_reel3", "stopped"));
        f.FinishAnimation();
        Assert.AreEqual(CasinoMoney.Format(spin.Payout), f.Text("payout"));
        Assert.AreEqual(f.Text("bankroll"), f.Text("bankroll_footer"));
        Assert.AreEqual(spin.State.FreeSpinsRemaining.ToString(), f.Text("bb_remaining"));
        for (var cell = 0; cell < 15; cell++)
            Assert.IsTrue(f.PanelClass("bb_symbol" + cell, "bb-symbol-" + (int)spin.Grid[cell]));
        await f.Click("free");
        Assert.IsTrue(f.Class("reels-armed"));
        Assert.IsFalse(f.PanelClass("bb_reel0", "stopped"));
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.IsTrue(f.Class("motion-b"));
        Assert.AreEqual(2, f.Rounds.Calls.Count, "Animation must never create a second booking.");
    }

    [TestMethod]
    public async Task EveryRoundGameOpensWithRulesAndBookShowsPreviewBeforeBooking()
    {
        using var f = await Fixture.Create();
        foreach (var game in Enum.GetValues<CasinoRoundGame>())
        {
            Assert.IsTrue(await f.Presenter.OpenAsync(Player, game));
            Assert.AreEqual(1, f.Hud.VisiblePlayers(f.ActiveHud).Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(f.Text("detail")));
            if (game == CasinoRoundGame.Book)
                for (var index = 0; index < 15; index++)
                    Assert.IsTrue(f.PanelClass("bb_symbol" + index, "bb-symbol-" + index % 8));
            Assert.AreEqual(0, f.Rounds.Calls.Count, "Opening a game must not place a stake.");
            f.Presenter.Close(Player);
        }
    }

    [TestMethod]
    public async Task StartAndBankUseStoredRoundRevision_AndAnimationCannotRepeatSettlement()
    {
        using var f = await Fixture.Create();
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        await f.Click("plus");
        await f.Click("play");
        Assert.AreEqual(150, f.Rounds.Last!.Stake);
        Assert.AreEqual(CasinoRoundAction.Start, f.Rounds.Last.Action);
        Assert.AreEqual("850 €", f.Text("bankroll"));
        await f.Click("bank");
        Assert.AreEqual(1, f.Rounds.Calls.Count, "Animation must block subsequent actions.");
        f.FinishAnimation();
        var round = f.Rounds.Current!;
        await f.Click("bank");
        Assert.AreEqual(round.Id, f.Rounds.Last.SessionId);
        Assert.AreEqual(1L, f.Rounds.Last.ExpectedRevision);
        Assert.AreEqual(CasinoRoundAction.Bank, f.Rounds.Last.Action);
        Assert.AreEqual("0 €", f.Text("payout"), "Payout is concealed until the animation ends.");
        f.FinishAnimation();
        f.Presenter.Tick();
        Assert.AreEqual("150 €", f.Text("payout"));
        Assert.AreEqual(1000L, f.Wallet.Balance.Allocated);
        Assert.AreEqual(2, f.Rounds.Receipts.Count);
    }

    [TestMethod]
    public async Task UncertainReceiptBlocksNewActions_AndRetryReusesEntireOperation()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        f.Wallet.FailRead = true;
        await f.Click("play");
        var operation = f.Rounds.Last!;
        Assert.IsTrue(f.Class("uncertain"));
        await f.Click("play");
        await f.Click("plus");
        Assert.AreEqual(1, f.Rounds.Calls.Count);
        f.Wallet.FailRead = false;
        await f.Click("retry");
        Assert.AreEqual(operation, f.Rounds.Last);
        Assert.AreEqual(1, f.Rounds.Receipts.Count);
        Assert.AreEqual(900L, f.Wallet.Balance.Allocated);
        Assert.IsFalse(f.Class("uncertain"));
    }

    [TestMethod]
    public async Task RejectedStartCanBeRetriedWithNewOperation()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        f.Rounds.RejectOnce = new CasinoRejectedException("Nicht genügend Automatenguthaben.");
        await f.Click("play");
        var rejected = f.Rounds.Last!;
        Assert.IsFalse(f.Class("uncertain"));
        Assert.AreEqual("Nicht genügend Automatenguthaben.", f.Text("status"));
        await f.Click("play");
        Assert.AreNotEqual(rejected.Id, f.Rounds.Last!.Id);
        Assert.AreEqual(1, f.Rounds.Receipts.Count);
    }

    [TestMethod]
    public async Task ReconnectRestoresActiveGame_AndBackReturnsToLobby()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        await f.Click("play");
        var active = f.Rounds.Current;
        await f.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack));
        Assert.AreEqual("RISK · DOUBLE OR BANK", f.Text("title"));
        Assert.AreEqual("Gespeicherte Runde fortsetzen", f.Text("status"));
        Assert.AreEqual(active, f.Rounds.Current);
        await f.Click("back");
        Assert.AreEqual(1, f.BackCalls);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        await f.Click("bank");
        Assert.AreEqual(1, f.Rounds.Calls.Count, "A hidden HUD must not accept actions.");
    }

    [TestMethod]
    public async Task RevokedPermissionClosesHud_AndCannotCreateRound()
    {
        using var f = await Fixture.Create();
        f.Permission.Allowed = false;
        Assert.IsFalse(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        Assert.AreEqual(0, f.BeforeOpenCalls);
        f.Permission.Allowed = true;
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        Assert.AreEqual(1, f.BeforeOpenCalls);
        f.Permission.Allowed = false;
        await f.Click("play");
        Assert.AreEqual(0, f.Rounds.Calls.Count);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task DelayedOpenCannotShowHudForReplacedPlayerSession()
    {
        using var f = await Fixture.Create();
        var delayed = new TaskCompletionSource<DurableCasinoRoundSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Rounds.DelayedRead = delayed.Task;
        var open = f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk).AsTask();
        await f.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        delayed.SetResult(null);
        Assert.IsFalse(await open);
        Assert.AreEqual(0, f.BeforeOpenCalls);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task CloseDuringBookingDoesNotReleasePendingGuardOrReadAnotherSnapshot()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Rounds.DelayedApply = delayed.Task;
        var booking = f.Click("play").AsTask();
        try
        {
            await f.Click("close");
            Assert.AreEqual(1, f.Rounds.ReadCalls, "Closing must not start another read while booking is pending.");
            Assert.IsFalse(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack));
        }
        finally
        {
            delayed.SetResult(true);
            await booking;
        }
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task StakeBoundsAndActiveRoundPreventChangingReservedStake()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        for (var i = 0; i < 20; i++) await f.Click("minus");
        Assert.AreEqual("50 €", f.Text("stake"));
        for (var i = 0; i < 20; i++) await f.Click("plus");
        Assert.AreEqual("500 €", f.Text("stake"));
        await f.Click("play");
        f.FinishAnimation();
        await f.Click("minus");
        Assert.AreEqual("500 €", f.Text("stake"));
        Assert.IsFalse(await f.Presenter.OpenAsync(Player, (CasinoRoundGame)99));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(4);
        public void AdvanceMilliseconds(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
    }

    private sealed class Permission : IPermissionEvaluator
    {
        public bool Allowed = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId player, PermissionId permission, CancellationToken token = default)
            => ValueTask.FromResult(Allowed);
    }

    private sealed class Wallet : ICasinoWallet
    {
        public CasinoBalance Balance = new(2000, 1000, 0);
        public bool FailRead;
        public ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default)
            => FailRead ? ValueTask.FromException<CasinoBalance>(new InvalidOperationException("Connection lost")) : ValueTask.FromResult(Balance);
        public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation operation, CancellationToken token = default)
            => throw new AssertFailedException("Round presenter must use the durable rounds coordinator for mutations.");
        public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<CasinoReceipt>>([]);
    }

    private sealed class Rounds(Wallet wallet) : ICasinoRounds
    {
        public DurableCasinoRoundSnapshot? Current;
        public DurableCasinoRoundSnapshot? NextSnapshot;
        public DurableCasinoRoundOperation? Last;
        public Exception? RejectOnce;
        public Task<DurableCasinoRoundSnapshot?>? DelayedRead;
        public Task? DelayedApply;
        public int ReadCalls;
        public List<DurableCasinoRoundOperation> Calls { get; } = [];
        public Dictionary<Guid, DurableCasinoRoundReceipt> Receipts { get; } = [];
        public ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default)
        {
            ReadCalls++;
            return DelayedRead is { } delayed ? new(delayed) : ValueTask.FromResult(Current is { IsComplete: false } ? Current : null);
        }
        public async ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default)
        {
            if (DelayedApply is { } delayed) await delayed;
            Last = operation;
            Calls.Add(operation);
            if (RejectOnce is { } rejected) { RejectOnce = null; throw rejected; }
            if (Receipts.TryGetValue(operation.Id, out var replay)) return replay;
            if (NextSnapshot is { } next)
            {
                Current = next;
                var prepared = new DurableCasinoRoundReceipt(operation.Id, wallet.Balance, next);
                Receipts.Add(operation.Id, prepared);
                return prepared;
            }
            var start = operation.Action == CasinoRoundAction.Start;
            var previous = Current is null ? null : JsonSerializer.Deserialize<RiskRoundState>(Current.StateJson);
            var state = start ? RiskRules.Start(operation.Stake) : RiskRules.Bank(previous!, previous!.Revision);
            var complete = state.Status != RiskRoundStatus.Active;
            var payout = state.FinalPayout ?? 0;
            wallet.Balance = wallet.Balance with
            {
                Allocated = wallet.Balance.Allocated - (start ? operation.Stake : 0) + payout,
                Revision = wallet.Balance.Revision + 1,
            };
            Current = new(operation.SessionId, operation.Player, operation.Game, operation.Stake,
                (Current?.Revision ?? 0) + 1, complete, operation.Stake, payout, JsonSerializer.Serialize(state));
            var receipt = new DurableCasinoRoundReceipt(operation.Id, wallet.Balance, Current);
            Receipts.Add(operation.Id, receipt);
            return receipt;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new();
        public readonly Permission Permission = new();
        public readonly Wallet Wallet = new();
        public readonly TestCustomHudService Hud = new();
        public readonly PlayerRegistry Players;
        public readonly CasinoRoundPresenter Presenter;
        public readonly Rounds Rounds;
        public int BeforeOpenCalls;
        public int BackCalls;
        public readonly List<PlayerId> ReelClicks = [];

        private Fixture(CasinoStakeLimits? stakeLimits = null, int biggestMaximum = 10000)
        {
            var events = new AnoEventBus();
            Players = new(events);
            Rounds = new(Wallet);
            Presenter = new(Hud, Players, Permission, Wallet, Rounds, events, _ => BeforeOpenCalls++,
                (_, _) => { BackCalls++; return ValueTask.CompletedTask; }, Clock, stakeLimits: stakeLimits,
                reelStopSound: playerId => ReelClicks.Add(playerId), biggestBoyMaximumStake: biggestMaximum);
        }

        public static async Task<Fixture> Create(CasinoStakeLimits? stakeLimits = null, int biggestMaximum = 10000)
        {
            var f = new Fixture(stakeLimits, biggestMaximum);
            await f.Players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
            return f;
        }

        public AnoCore.Abstractions.Hud.CustomHudId ActiveHud => Hud.VisiblePlayers(CasinoRoundPresenter.ExpandingHudId).Contains(Player) ? CasinoRoundPresenter.ExpandingHudId : CasinoRoundPresenter.HudId;
        public ValueTask Click(string action) => Hud.ClickAsync(Player, ActiveHud, "casino_round_" + action);
        public string? Text(string panel) => Hud.Text(Player, ActiveHud, "casino_round_" + panel);
        public bool Class(string name) => Hud.Class(Player, ActiveHud, "casino_round_root", name);
        public bool PanelClass(string panel, string name) => Hud.Class(Player, ActiveHud, "casino_round_" + panel, name);
        public void FinishAnimation() { if (ActiveHud == CasinoRoundPresenter.ExpandingHudId) Clock.AdvanceMilliseconds(4200); else Clock.Advance(); Presenter.Tick(); }
        public void Dispose() => Presenter.Dispose();
    }
}
