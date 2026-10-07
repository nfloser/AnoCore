using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Menus;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Menus;

[TestClass]
public sealed class MenuServiceTests
{
    private static readonly PlayerId Player = new(76561198000000992);
    private static readonly ModuleId Owner = new("tests");

    [TestMethod]
    public void OpenRevisionsDistinguishReopensAndChangesSurviveFailingObservers()
    {
        var service = new MenuService();
        var changes = new List<PlayerId>();
        service.Changed += _ => throw new InvalidOperationException("observer");
        service.Changed += changes.Add;
        using var registration = service.Register(Owner, new MenuDefinition(new MenuId("ano.revision"), "Revision", []));
        service.Open(Player, new MenuId("ano.revision"));
        var revision = service.GetOpenRevision(Player);
        Assert.IsTrue(service.Close(Player));
        Assert.AreEqual(0L, service.GetOpenRevision(Player));
        service.Open(Player, new MenuId("ano.revision"));
        Assert.IsTrue(service.GetOpenRevision(Player) > revision);
        registration.Dispose();
        Assert.AreEqual(0L, service.GetOpenRevision(Player));
        Assert.HasCount(4, changes);
    }

    [TestMethod]
    public void MenuId_RejectsEmptyAndNonAnoNamespaces()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MenuId(""));
        Assert.ThrowsExactly<ArgumentException>(() => new MenuId("menu.test"));
    }

    [TestMethod]
    public async Task SelectAsync_InvokesExactlyOnceAndClosesSession()
    {
        var selections = 0;
        var service = new MenuService();
        service.Register(Owner, new MenuDefinition(
            new MenuId("ano.test"),
            "Choose",
            [new MenuOption("one", "One", _ => { selections++; return ValueTask.CompletedTask; })]));
        service.Open(Player, new MenuId("ano.test"));

        var first = await service.SelectAsync(Player, "one");
        var second = await service.SelectAsync(Player, "one");

        Assert.IsTrue(first.Accepted);
        Assert.IsFalse(second.Accepted);
        Assert.AreEqual(1, selections);
        Assert.IsFalse(service.TryGetOpenMenu(Player, out _));
    }

    [TestMethod]
    public async Task SelectAsync_RejectsStaleDefinitionWhenSameMenuIdIsReRegistered()
    {
        var service = new MenuService();
        var firstSelections = 0;
        var secondSelections = 0;
        var first = new MenuDefinition(new MenuId("ano.test"), "First",
            [new MenuOption("one", "One", _ =>
            {
                firstSelections++;
                return ValueTask.CompletedTask;
            })]);
        using var registration = service.Register(Owner, first);
        service.Open(Player, first.Id);
        registration.Dispose();

        var second = new MenuDefinition(first.Id, "Second",
            [new MenuOption("one", "One", _ =>
            {
                secondSelections++;
                return ValueTask.CompletedTask;
            })]);
        using var replacement = service.Register(Owner, second);
        service.Open(Player, second.Id);

        var stale = await service.SelectAsync(Player, first, "one");
        Assert.IsFalse(stale.Accepted);
        Assert.IsTrue(service.TryGetOpenMenu(Player, out var stillOpen));
        Assert.AreSame(second, stillOpen);
        Assert.AreEqual(0, firstSelections);
        Assert.AreEqual(0, secondSelections);

        var current = await service.SelectAsync(Player, second, "one");
        Assert.IsTrue(current.Accepted);
        Assert.AreEqual(1, secondSelections);
    }

    [TestMethod]
    public void Register_RejectsDuplicateMenuIds()
    {
        var service = new MenuService();
        service.Register(Owner, new MenuDefinition(new MenuId("ano.test"), "One", []));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            service.Register(new ModuleId("other"), new MenuDefinition(new MenuId("ano.test"), "Two", [])));
    }

    [TestMethod]
    public void UnregisterAll_ClosesOpenMenusOwnedByModule()
    {
        var service = new MenuService();
        service.Register(Owner, new MenuDefinition(new MenuId("ano.test"), "One", []));
        service.Open(Player, new MenuId("ano.test"));

        service.UnregisterAll(Owner);

        Assert.IsFalse(service.TryGetOpenMenu(Player, out _));
        Assert.ThrowsExactly<KeyNotFoundException>(() => service.Open(Player, new MenuId("ano.test")));
    }
}
