using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Placeholders;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ChatMessageFormatterTests
{
    private static readonly PlayerId Player = new(76561198000012621);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-chat-format-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task CreateAndFormat_ResolveRankTagBeforeSafeDataSubstitution()
    {
        var placeholders = new PlaceholderRegistry();
        using var rank = placeholders.Register(new ModuleId("test"), "rank.tag",
            (context, _) => ValueTask.FromResult<string?>(
                context.Values["player"] is PlayerId id && id == Player ? "[Elite]" : null));
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);

        var formatted = await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Nils", "hello {rank.tag}", false));

        Assert.AreEqual("[Elite] Nils: hello {rank.tag}", formatted);
    }

    [TestMethod]
    public async Task Format_UsesSeparateTeamAndPublicTemplates()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            PublicTemplate = "ALL {player.name}>{message}",
            TeamTemplate = "TEAM {player.name}>{message}",
        });
        var formatter = await ChatMessageFormatter.CreateAsync(
            store, new PlaceholderRegistry());

        Assert.AreEqual("ALL Player>one", await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Player", "one", false)));
        Assert.AreEqual("TEAM Player>two", await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Player", "two", true)));
    }

    [TestMethod]
    public async Task Format_SanitizesAndBoundsUntrustedFields()
    {
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), new PlaceholderRegistry());

        var formatted = await formatter.FormatAsync(new ChatFormatRequest(
            Player, new string('n', 60) + "\n", new string('m', 300) + "\r", false));

        Assert.IsFalse(formatted.Any(char.IsControl));
        StringAssert.Contains(formatted, new string('n', 48));
        StringAssert.Contains(formatted, new string('m', 256));
        Assert.IsFalse(formatted.Contains(new string('n', 49), StringComparison.Ordinal));
        Assert.IsFalse(formatted.Contains(new string('m', 257), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Format_AppliesValidatedSegmentColorsAndResets()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            RankColor = "Green",
            NameColor = "Team",
            MessageColor = "Yellow",
        });
        var placeholders = new PlaceholderRegistry();
        using var rank = placeholders.Register(new ModuleId("test"), "rank.tag",
            (_, _) => ValueTask.FromResult<string?>("[Elite]"));
        var formatter = await ChatMessageFormatter.CreateAsync(store, placeholders);
        var player = new PlayerSnapshot(
            Player,
            PlayerSessionId.New(),
            "Nils",
            isConnected: true,
            isAlive: true,
            PlayerTeam.CounterTerrorist,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        var prepared = await formatter.PrepareAsync(player);
        var formatted = prepared.Format("hello", false);

        Assert.AreEqual(
            "\x04[Elite]\x01 \x0BNils\x01: \x09hello\x01",
            formatted);
    }

    [TestMethod]
    public async Task Format_EmptyRankTagDoesNotEmitOrphanedColorCodes()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            RankColor = "Green",
        });
        var formatter = await ChatMessageFormatter.CreateAsync(
            store, new PlaceholderRegistry());

        var formatted = await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Player", "hello", false));

        Assert.IsFalse(formatted.Contains('\x04'));
        Assert.IsFalse(formatted.Contains('\x01'));
        Assert.AreEqual(" Player: hello", formatted);
    }

    [TestMethod]
    public async Task Format_UserControlCharactersCannotInjectColors()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            MessageColor = "Green",
        });
        var formatter = await ChatMessageFormatter.CreateAsync(
            store, new PlaceholderRegistry());

        var formatted = await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Player\x07", "hello\x10red", false));

        Assert.AreEqual(1, formatted.Count(character => character == '\x04'));
        Assert.AreEqual(1, formatted.Count(character => character == '\x01'));
        Assert.IsFalse(formatted.Contains('\x07'));
        Assert.IsFalse(formatted.Contains('\x10'));
        StringAssert.Contains(formatted, "Player ");
        StringAssert.Contains(formatted, "hello red");
    }

    [TestMethod]
    public async Task Create_RejectsUnknownColorNames()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            RankColor = "rainbow",
        });

        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await ChatMessageFormatter.CreateAsync(
                store, new PlaceholderRegistry()));
    }

    [TestMethod]
    public async Task Format_HonorsCancellationDuringPlaceholderResolution()
    {
        var placeholders = new PlaceholderRegistry();
        using var pending = placeholders.Register(new ModuleId("test"), "rank.tag",
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "never";
            });
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await formatter.FormatAsync(
                new ChatFormatRequest(Player, "Player", "text", false),
                cancellation.Token));
    }

    [TestMethod]
    public async Task Create_RejectsMissingOrRepeatedRequiredTokens()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            PublicTemplate = "{player.name}: no message token",
            TeamTemplate = "{player.name} {message} {message}",
        });

        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await ChatMessageFormatter.CreateAsync(store, new PlaceholderRegistry()));
    }

    [TestMethod]
    public void Validate_RejectsOversizedAndControlCharacterTemplates()
    {
        var configuration = new ChatFormatConfiguration
        {
            PublicTemplate = "{player.name}: {message}\n",
            TeamTemplate = "{player.name}: {message}" + new string('x', 240),
        };

        var errors = ChatFormatConfiguration.Validate(configuration);

        Assert.AreEqual(2, errors.Count);
    }
}
