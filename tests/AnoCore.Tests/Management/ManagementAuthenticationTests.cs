using AnoCore.Abstractions.Management;
using AnoCore.Runtime.Management;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class ManagementAuthenticationTests
{
    private const string Secret = "this-is-a-long-management-secret-12345";

    [TestMethod]
    public void TokenHasher_VerifiesCorrectSecretAndRejectsWrongSecret()
    {
        var credential = ManagementTokenHasher.Create(
            "ops-primary",
            Secret,
            ManagementScope.ReadStatus | ManagementScope.ManageServer,
            iterations: 100_000);

        Assert.IsTrue(ManagementTokenHasher.Verify(credential, Secret));
        Assert.IsFalse(ManagementTokenHasher.Verify(
            credential, "this-is-a-long-management-secret-wrong"));
        Assert.AreNotEqual(Secret, credential.HashBase64);
        Assert.AreNotEqual(Secret, credential.SaltBase64);
    }

    [TestMethod]
    public void PersistedCredential_ReconstructsWithoutPlaintextSecret()
    {
        var created = ManagementTokenHasher.Create(
            "panel",
            Secret,
            ManagementScope.ReadStatus,
            iterations: 100_000);

        var restored = new ManagementTokenCredential(
            created.TokenId,
            created.Scopes,
            created.SaltBase64,
            created.HashBase64,
            created.Iterations);

        Assert.IsTrue(ManagementTokenHasher.Verify(restored, Secret));
        Assert.IsFalse(restored.ToString().Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Authenticator_ReturnsScopedPrincipalAndRejectsUnknownCredentials()
    {
        var credential = ManagementTokenHasher.Create(
            "panel",
            Secret,
            ManagementScope.ReadStatus | ManagementScope.ManagePlayers,
            iterations: 100_000);
        var authenticator = new ManagementTokenAuthenticator([credential]);

        var principal = authenticator.Authenticate("panel", Secret);

        Assert.IsNotNull(principal);
        Assert.IsTrue(principal.Has(ManagementScope.ReadStatus));
        Assert.IsTrue(principal.Has(ManagementScope.ManagePlayers));
        Assert.IsFalse(principal.Has(ManagementScope.ManageServer));
        Assert.IsNull(authenticator.Authenticate("unknown", Secret));
        Assert.IsNull(authenticator.Authenticate("panel", "wrong-but-still-long-enough-secret-1234"));
    }

    [TestMethod]
    public void Authenticator_RejectsDuplicateTokenIds()
    {
        var first = ManagementTokenHasher.Create(
            "duplicate", Secret, ManagementScope.ReadStatus, iterations: 100_000);
        var second = ManagementTokenHasher.Create(
            "duplicate", Secret + "-second", ManagementScope.ManageServer, iterations: 100_000);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new ManagementTokenAuthenticator([first, second]));
    }
}
