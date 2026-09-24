using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class WarningContractTests
{
    private static readonly PlayerId Target = new(76561198000009101);
    private static readonly PlayerId Actor = new(76561198000009102);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Warning_ExpiresAtBoundaryAndClearRetainsHistory()
    {
        var warning = new WarningRecord(Guid.NewGuid(), Target, null, "  repeated spam  ",
            Now, Now.AddMinutes(10));
        Assert.AreEqual("repeated spam", warning.Reason);
        Assert.IsTrue(warning.IsActiveAt(Now.AddMinutes(10).AddTicks(-1)));
        Assert.IsFalse(warning.IsActiveAt(Now.AddMinutes(10)));
        var cleared = warning.Clear(Actor, "  appeal upheld  ", Now.AddMinutes(5));
        Assert.IsNull(warning.ClearedAtUtc);
        Assert.AreEqual("appeal upheld", cleared.ClearReason);
        Assert.IsFalse(cleared.IsActiveAt(Now.AddMinutes(5)));
        Assert.IsTrue(cleared.IsActiveAt(Now.AddMinutes(5).AddTicks(-1)));
    }

    [TestMethod]
    public void Warning_RejectsInvalidExpiryAndOrphanedClearMetadata()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new WarningRecord(Guid.NewGuid(), Target, Actor, "reason", Now, Now));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new WarningRecord(Guid.Empty, Target, Actor, "reason", Now));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new WarningRecord(Guid.NewGuid(), Target, Actor, " ", Now));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new WarningRecord(Guid.NewGuid(), Target, Actor,
                new string('x', WarningValidation.MaxReasonLength + 1), Now));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new WarningRecord(Guid.NewGuid(), Target, Actor, "reason", Now,
                clearedById: Actor));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new WarningRecord(Guid.NewGuid(), Target, Actor, "reason", Now,
                clearedAtUtc: Now.AddTicks(-1), clearReason: "reason"));
    }

    [TestMethod]
    public void Warning_RejectsSecondClearAndClearAfterExpiry()
    {
        var warning = new WarningRecord(Guid.NewGuid(), Target, Actor, "reason",
            Now, Now.AddMinutes(2));
        var cleared = warning.Clear(null, "resolved", Now.AddMinutes(1));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            cleared.Clear(Actor, "again", Now.AddMinutes(1)));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            warning.Clear(Actor, "late", Now.AddMinutes(2)));
    }
}
