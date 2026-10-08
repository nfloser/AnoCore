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
    public async Task ExternalCommandPolicyReloadsAtomicallyAndInvalidCandidateKeepsPreviousNames()
    {
        var store = new JsonConfigStore(_root);
        var reloads = new ConfigReloadRegistry();
        using var formatter = await ChatMessageFormatter.CreateAsync(store, new PlaceholderRegistry(), reloads: reloads);
        Assert.IsTrue(formatter.IsPassthroughCommand(".map 123456789"));
        var next = ChatFormatConfiguration.Default;
        next.PassthroughCommands = [".custom"];
        await store.SaveAsync("chat-format", next);
        await reloads.ReloadAsync("chat-format");
        Assert.IsFalse(formatter.IsPassthroughCommand(".map 123456789"));
        Assert.IsTrue(formatter.IsPassthroughCommand(".CUSTOM argument"));
        Assert.IsFalse(formatter.IsPassthroughCommand(".customx"));
        next.PassthroughCommands = [".map", ".map;quit"];
        await store.SaveAsync("chat-format", next);
        await Assert.ThrowsAsync<Exception>(async () => await reloads.ReloadAsync("chat-format"));
        Assert.IsTrue(formatter.IsPassthroughCommand(".custom"));
        Assert.IsFalse(formatter.IsPassthroughCommand(".map 123456789"));
    }

    [TestMethod]
    [DataRow(".map argument")]
    [DataRow("map")]
    [DataRow(".map;quit")]
    [DataRow(".")]
    [DataRow(".1map")]
    public void ExternalCommandPolicyRejectsNonTokens(string command)
    {
        var configuration = ChatFormatConfiguration.Default;
        configuration.PassthroughCommands = [command];
        Assert.IsNotEmpty(ChatFormatConfiguration.Validate(configuration));
    }

    [TestMethod]
    public void ExternalCommandPolicyRejectsNullDuplicateAndOversizedCatalogsButCanBeDisabled()
    {
        var configuration = ChatFormatConfiguration.Default;
        configuration.PassthroughCommands = null!;
        Assert.IsNotEmpty(ChatFormatConfiguration.Validate(configuration));
        configuration.PassthroughCommands = [".map", ".MAP"];
        Assert.IsNotEmpty(ChatFormatConfiguration.Validate(configuration));
        configuration.PassthroughCommands = Enumerable.Range(0, 65).Select(index => $".cmd{index}").ToList();
        Assert.IsNotEmpty(ChatFormatConfiguration.Validate(configuration));
        configuration.PassthroughCommands = [];
        Assert.IsEmpty(ChatFormatConfiguration.Validate(configuration));
        Assert.IsFalse(configuration.IsPassthroughCommand(".map 123456789"));
    }

    [TestMethod]
    public async Task Format_LegacyRankTagUsesPrioritizedChatTag()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            PublicTemplate = "{rank.tag} {player.name}: {message}",
            TeamTemplate = "{rank.tag} {player.name}: {message}",
            RankColor = "Green",
        });
        var placeholders = new PlaceholderRegistry();
        using var rank = placeholders.RegisterPrioritized(
            new ModuleId("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));
        using var staff = placeholders.RegisterPrioritized(
            new ModuleId("staff"), "chat.tag", 100,
            (_, _) => ValueTask.FromResult<string?>("[Staff]"));
        var formatter = await ChatMessageFormatter.CreateAsync(store, placeholders);

        var formatted = await formatter.FormatAsync(
            new ChatFormatRequest(Player, "Nils", "hello", false));

        Assert.AreEqual("\x04[Staff]\x01 Nils: hello", formatted);
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
        var placeholders = new PlaceholderRegistry();
        using var rank = placeholders.Register(
            new ModuleId("test"),
            "rank.tag",
            (_, _) => ValueTask.FromResult<string?>(string.Empty));
        var formatter = await ChatMessageFormatter.CreateAsync(
            store, placeholders);

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
