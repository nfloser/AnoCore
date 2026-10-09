using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class NativeChatTextTests
{
    [TestMethod]
    [DataRow("\x09[DEV]\x01 Nille: test")]
    [DataRow("\x0E[HOST]\x01 Nille: test")]
    [DataRow("\x04[ANOMEME]\x01 Nille: test")]
    [DataRow("\x07[FOUNDER]\x01 Nille: test")]
    public void NativeOutputStartsWithSpaceAndPreservesTrustedColorControls(string text)
        => Assert.AreEqual(" " + text, NativeChatText.Prepare(text));

    [TestMethod]
    public void PreparingAlreadyPrefixedOutputDoesNotAccumulateSpaces()
        => Assert.AreEqual(" \x09[DEV]\x01 Nille: test",
            NativeChatText.Prepare(NativeChatText.Prepare("\x09[DEV]\x01 Nille: test")));
}
