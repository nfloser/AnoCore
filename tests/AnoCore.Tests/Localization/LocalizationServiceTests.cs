using AnoCore.Runtime.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Localization;

[TestClass]
public sealed class LocalizationServiceTests
{
    [TestMethod]
    public void Translate_UsesRequestedLocaleThenFallback()
    {
        var service = CreateService();

        Assert.AreEqual("Hallo", service.Translate("hello", "de-DE"));
        Assert.AreEqual("Goodbye", service.Translate("bye", "de-DE"));
    }

    [TestMethod]
    public void Translate_MissingKeyReturnsVisibleMarker()
    {
        var service = CreateService();

        Assert.AreEqual("[[missing.key]]", service.Translate("missing.key", "de"));
    }

    [TestMethod]
    public void Translate_ReplacesNamedArgumentsInvariantly()
    {
        var service = CreateService();

        var translated = service.Translate(
            "welcome",
            "en",
            new Dictionary<string, object?> { ["player"] = "Nille", ["count"] = 3 });

        Assert.AreEqual("Welcome Nille (3)", translated);
    }

    private static LocalizationService CreateService()
        => new(
            "en",
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string>
                {
                    ["hello"] = "Hello",
                    ["bye"] = "Goodbye",
                    ["welcome"] = "Welcome {player} ({count})",
                },
                ["de"] = new Dictionary<string, string>
                {
                    ["hello"] = "Hallo",
                },
            });
}
