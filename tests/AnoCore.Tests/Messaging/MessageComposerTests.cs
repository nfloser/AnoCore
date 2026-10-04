using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Localization;
using AnoCore.Runtime.Messaging;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Messaging;

[TestClass]
public sealed class MessageComposerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId English = new(76561198000215001);
    private static readonly PlayerId German = new(76561198000215002);

    [TestMethod]
    public void Compose_UsesLocaleFallbackAndSanitizesArguments()
    {
        var localization = new LocalizationConfiguration
        {
            FallbackLocale = "en",
            Catalogs = new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(StringComparer.Ordinal)
                {
                    ["notice"] = "Hello {player}",
                },
                ["de"] = new(StringComparer.Ordinal)
                {
                    ["notice"] = "Hallo {player}",
                },
            },
        }.CreateService();
        var composer = new MessageComposer(localization);

        var german = composer.Compose(
            MessageContent.Localized("notice",
                new Dictionary<string, object?> { ["player"] = "Ni\nlle" }),
            "de-DE");
        var fallback = composer.Compose(
            MessageContent.Localized("notice",
                new Dictionary<string, object?> { ["player"] = "Nille" }),
            "fr-FR");

        Assert.AreEqual("Hallo Ni lle", german);
        Assert.AreEqual("Hello Nille", fallback);
    }

    [TestMethod]
    public void Compose_BoundsLocalizedOutputAndRawRejectsControls()
    {
        var localization = new LocalizationConfiguration
        {
            FallbackLocale = "en",
            Catalogs = new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(StringComparer.Ordinal)
                {
                    ["long"] = "{value}",
                },
            },
        }.CreateService();
        var composer = new MessageComposer(localization);

        var result = composer.Compose(
            MessageContent.Localized(
                "long",
                new Dictionary<string, object?>
                {
                    ["value"] = new string('x', MessageComposer.MaximumOutputLength + 200),
                }));

        Assert.AreEqual(MessageComposer.MaximumOutputLength, result.Length);
        Assert.ThrowsExactly<ArgumentException>(() =>
            MessageContent.Raw("unsafe\nmessage"));
    }

    [TestMethod]
    public async Task MessageService_UsesPerPlayerLocaleAndCurrentSessionSnapshots()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var english = await players.ConnectAsync(
            new PlayerConnection(
                English, "English", PlayerTeam.Terrorist, true, Now));
        var german = await players.ConnectAsync(
            new PlayerConnection(
                German, "German", PlayerTeam.CounterTerrorist, true, Now));
        var localization = new LocalizationConfiguration
        {
            FallbackLocale = "en",
            Catalogs = new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(StringComparer.Ordinal) { ["hello"] = "Hello" },
                ["de"] = new(StringComparer.Ordinal) { ["hello"] = "Hallo" },
            },
        }.CreateService();
        var transport = new RecordingTransport();
        var service = new MessageService(
            players,
            new MessageComposer(localization),
            new TestLocales(),
            transport);

        var delivered = await service.BroadcastAsync(
            MessageChannel.Chat,
            MessageContent.Localized("hello"));

        Assert.AreEqual(2, delivered);
        Assert.AreEqual(
            (english.SessionId, MessageChannel.Chat, "Hello"),
            transport.Messages.Single(value => value.Player == English).Value);
        Assert.AreEqual(
            (german.SessionId, MessageChannel.Chat, "Hallo"),
            transport.Messages.Single(value => value.Player == German).Value);
    }

    [TestMethod]
    public async Task MessageService_DisconnectedPlayerIsNotQueued()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var current = await players.ConnectAsync(
            new PlayerConnection(
                English, "English", PlayerTeam.Terrorist, true, Now));
        await players.DisconnectAsync(English, current.SessionId, Now.AddSeconds(1));
        var transport = new RecordingTransport();
        var localization = LocalizationConfiguration.Default.CreateService();
        var service = new MessageService(
            players,
            new MessageComposer(localization),
            new FixedMessageLocaleResolver("en"),
            transport);

        var accepted = await service.SendAsync(
            English,
            MessageChannel.Center,
            MessageContent.Raw("Hello"));

        Assert.IsFalse(accepted);
        Assert.AreEqual(0, transport.Messages.Count);
    }

    private sealed class TestLocales : IMessageLocaleResolver
    {
        public string ServerLocale => "en";

        public string Resolve(PlayerSnapshot player)
            => player.Id == German ? "de-DE" : "en-US";
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        public List<(PlayerId Player,
            (PlayerSessionId Session, MessageChannel Channel, string Text) Value)> Messages { get; } = [];

        public ValueTask SendAsync(
            PlayerSnapshot expectedPlayer,
            MessageChannel channel,
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add((
                expectedPlayer.Id,
                (expectedPlayer.SessionId, channel, message)));
            return ValueTask.CompletedTask;
        }
    }
}
