using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class AdminAuditContractTests
{
    private static readonly PlayerId Actor = new(76561198000008001);
    private static readonly PlayerId Target = new(76561198000008002);

    [TestMethod]
    public void AdminActionId_NormalizesSafeIdentifiers()
    {
        var action = new AdminActionId("  Kick.Silent  ");

        Assert.AreEqual("kick.silent", action.Value);
        Assert.AreEqual("kick.silent", action.ToString());
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("1kick")]
    [DataRow("kick/player")]
    [DataRow("kick player")]
    public void AdminActionId_RejectsUnsafeIdentifiers(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AdminActionId(value));
    }

    [TestMethod]
    public void AdminActionId_RejectsIdentifiersLongerThanSchemaLimit()
    {
        var value = "a" + new string('b', AdminActionId.MaxLength);

        Assert.ThrowsExactly<ArgumentException>(() => new AdminActionId(value));
    }

    [TestMethod]
    public void AdminAuditEntry_NormalizesUtcAndReason()
    {
        var local = new DateTimeOffset(2026, 9, 22, 18, 30, 0, TimeSpan.FromHours(2));

        var entry = new AdminAuditEntry(
            Guid.NewGuid(),
            new AdminActionId("kick"),
            Actor,
            Target,
            "  repeated griefing  ",
            local);

        Assert.AreEqual("repeated griefing", entry.Reason);
        Assert.AreEqual(local.ToUniversalTime(), entry.OccurredAtUtc);
        Assert.AreEqual(Actor, entry.ActorId);
        Assert.AreEqual(Target, entry.TargetId);
    }

    [TestMethod]
    public void AdminAuditEntry_AllowsConsoleActorAndServerWideAction()
    {
        var entry = new AdminAuditEntry(
            Guid.NewGuid(),
            new AdminActionId("server.restart"),
            actorId: null,
            targetId: null,
            "scheduled maintenance",
            DateTimeOffset.UtcNow);

        Assert.IsNull(entry.ActorId);
        Assert.IsNull(entry.TargetId);
    }

    [TestMethod]
    public void AdminAuditEntry_RejectsEmptyIdBlankReasonAndOversizedReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AdminAuditEntry(
            Guid.Empty,
            new AdminActionId("kick"),
            Actor,
            Target,
            "reason",
            DateTimeOffset.UtcNow));

        Assert.ThrowsExactly<ArgumentException>(() => new AdminAuditEntry(
            Guid.NewGuid(),
            new AdminActionId("kick"),
            Actor,
            Target,
            "   ",
            DateTimeOffset.UtcNow));

        Assert.ThrowsExactly<ArgumentException>(() => new AdminAuditEntry(
            Guid.NewGuid(),
            new AdminActionId("kick"),
            Actor,
            Target,
            new string('x', AdminAuditValidation.MaxReasonLength + 1),
            DateTimeOffset.UtcNow));
    }
}
