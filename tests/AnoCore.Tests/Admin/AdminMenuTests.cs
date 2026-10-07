using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class AdminMenuTests
{
    private static readonly PlayerId Admin = new(76561198000315001);
    private static readonly PlayerId Target = new(76561198000315002);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");

    [TestMethod]
    public async Task ConfirmExecutesExistingCommandOnceWithSelectedTargetDurationReason()
    {
        using var fixture = await Fixture.Create();
        await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        await fixture.Menus.SelectAsync(Admin, "anomute");
        await fixture.Menus.SelectAsync(Admin, Target.SteamId64.ToString());
        await fixture.Menus.SelectAsync(Admin, "15");
        await fixture.Menus.SelectAsync(Admin, "Rule violation");
        fixture.Menus.TryGetOpenMenu(Admin, out var confirm);
        await fixture.Menus.SelectAsync(Admin, confirm!, "confirm");
        await fixture.Menus.SelectAsync(Admin, confirm!, "confirm");
        Assert.AreEqual(1, fixture.Calls);
        Assert.AreEqual(Target.SteamId64.ToString(), fixture.Last!.Get<string>("target"));
        Assert.AreEqual(15, fixture.Last.Get<int>("minutes"));
        Assert.AreEqual("Rule violation", fixture.Last.Get<string>("reason"));
    }

    [TestMethod]
    public async Task RevokedPermissionAndReconnectedTargetsCannotExecuteSavedConfirmation()
    {
        using var fixture = await Fixture.Create();
        await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        await fixture.Menus.SelectAsync(Admin, "anomute");
        await fixture.Menus.SelectAsync(Admin, Target.SteamId64.ToString());
        await fixture.Menus.SelectAsync(Admin, "15");
        await fixture.Menus.SelectAsync(Admin, "Rule violation");
        fixture.Gateway.Allowed = false;
        await fixture.Menus.SelectAsync(Admin, "confirm");
        Assert.AreEqual(0, fixture.Calls);
        fixture.Gateway.Allowed = true;
        await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        await fixture.Menus.SelectAsync(Admin, "anomute");
        await fixture.Menus.SelectAsync(Admin, Target.SteamId64.ToString());
        await fixture.Menus.SelectAsync(Admin, "15");
        await fixture.Menus.SelectAsync(Admin, "Rule violation");
        await fixture.Players.ConnectAsync(new(Target, "New session", PlayerTeam.CounterTerrorist, true, Now.AddMinutes(1)));
        await fixture.Menus.SelectAsync(Admin, "confirm");
        Assert.AreEqual(0, fixture.Calls);
    }

    [TestMethod]
    public async Task PermissionFilteringDisconnectAndUnloadReleaseMenusAndCommand()
    {
        using var fixture = await Fixture.Create();
        fixture.Gateway.Allowed = false;
        var denied = await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        Assert.AreEqual(CommandFailureReason.Forbidden, denied.FailureReason);
        Assert.IsFalse(fixture.Menus.TryGetOpenMenu(Admin, out _));
        fixture.Gateway.Allowed = true;
        await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        fixture.Players.TryGet(Admin, out var actor);
        await fixture.Players.DisconnectAsync(Admin, actor!.SessionId, Now.AddMinutes(1));
        Assert.IsFalse(fixture.Menus.TryGetOpenMenu(Admin, out _));
        fixture.Module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound, (await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin)).FailureReason);
    }

    [TestMethod]
    public async Task DelayedTargetReadCannotReopenClosedOrReplaceAnotherFeatureMenu()
    {
        using var fixture = await Fixture.Create();
        await fixture.Commands.ExecuteAsync("!anoadminmenu", Admin);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gateway.Barrier = release.Task;
        var pending = fixture.Menus.SelectAsync(Admin, "anomute").AsTask();
        var other = new MenuDefinition(new("ano.other"), "Other feature", [new("info", "Data", _ => ValueTask.CompletedTask)]);
        using var registration = fixture.Menus.Register(new("other"), other);
        fixture.Menus.Open(Admin, other.Id);
        release.SetResult();
        await pending;
        fixture.Menus.TryGetOpenMenu(Admin, out var current);
        Assert.AreSame(other, current);
        Assert.AreEqual(0, fixture.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        public PlayerRegistry Players { get; private set; } = null!;
        public CommandRegistry Commands { get; private set; } = null!;
        public MenuService Menus { get; } = new();
        public Gateway Gateway { get; } = new();
        public AdminMenuModule Module { get; private set; } = null!;
        public int Calls { get; private set; }
        public CommandContext? Last { get; private set; }
        private IDisposable _command = null!;
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            var events = new AnoEventBus();
            fixture.Players = new(events);
            fixture.Commands = new(fixture.Gateway);
            await fixture.Players.ConnectAsync(new(Admin, "Admin", PlayerTeam.Terrorist, true, Now));
            await fixture.Players.ConnectAsync(new(Target, "Target", PlayerTeam.CounterTerrorist, true, Now));
            fixture._command = fixture.Commands.Register(new("test"), new("anomute", "Mute", new("ano.admin.mute"),
                arguments: [new("target", CommandArgumentKind.String, "Target"), new("minutes", CommandArgumentKind.Int32, "Duration"),
                    new("reason", CommandArgumentKind.String, "Reason", required: false)]), context =>
                { fixture.Calls++; fixture.Last = context; return ValueTask.FromResult(CommandResult.Ok("committed")); });
            fixture.Module = new(fixture.Commands, fixture.Players, fixture.Menus, fixture.Gateway, fixture.Gateway, events);
            return fixture;
        }
        public void Dispose() { Module.Dispose(); _command.Dispose(); }
    }
    private sealed class Gateway : IPermissionEvaluator, ITargetAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public Task? Barrier { get; set; }
        public ValueTask<bool> HasPermissionAsync(PlayerId player, PermissionId permission, CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed);
        public async ValueTask<TargetAuthorizationDecision> AuthorizeAsync(PlayerId actor, PlayerSnapshot target, PermissionId permission,
            bool allowSelf = false, CancellationToken cancellationToken = default)
        {
            if (Barrier is not null) await Barrier;
            return Allowed && actor != target.Id ? TargetAuthorizationDecision.Allowed : TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.PermissionDenied);
        }
    }
}
