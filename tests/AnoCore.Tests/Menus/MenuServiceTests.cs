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
