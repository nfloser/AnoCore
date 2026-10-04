using System.Data.Common;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class MySqlTournamentMatchRepository : ITournamentMatchRepository
{
    private readonly IDatabase _database;

    public MySqlTournamentMatchRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<TournamentStoredMatch> StoreAsync(
        TournamentMatchConfiguration configuration,
        TournamentRecoverySnapshot snapshot,
        bool makeActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration, snapshot);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            // The singleton runtime row is also the store/replacement mutex. This keeps
            // concurrent creates/replacements deterministic even before a match row exists.
            await LockRuntimeAsync(connection, transaction, token).ConfigureAwait(false);

            var currentRevision = await ReadRevisionAsync(
                connection, transaction, configuration.MatchId, token).ConfigureAwait(false);
            var revision = checked((currentRevision ?? 0) + 1);

            await UpsertMatchAsync(
                connection, transaction, configuration, snapshot, revision, token)
                .ConfigureAwait(false);
            await ReplaceMembersAsync(
                connection, transaction, configuration, token).ConfigureAwait(false);

            if (makeActive)
                await SetActiveAsync(
                    connection, transaction, configuration.MatchId, token).ConfigureAwait(false);

            return new TournamentStoredMatch(configuration, snapshot, revision);
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<TournamentStoredMatch?> LoadAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        RequireMatchId(matchId);
        return _database.InTransactionAsync(
            (connection, transaction, token) =>
                ReadStoredAsync(connection, transaction, matchId, forUpdate: false, token),
            cancellationToken: cancellationToken);
    }

    public ValueTask<TournamentStoredMatch?> LoadActiveAsync(
        CancellationToken cancellationToken = default)
        => _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var active = await ReadActiveAsync(connection, transaction, forUpdate: false, token)
                .ConfigureAwait(false);
            return active is null
                ? null
                : await ReadStoredAsync(
                    connection, transaction, active.Value, forUpdate: false, token)
                    .ConfigureAwait(false);
        }, cancellationToken: cancellationToken);

    public ValueTask<TournamentStoredMatch> SaveSnapshotAsync(
        Guid matchId,
        long expectedRevision,
        TournamentRecoverySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        RequireRevision(expectedRevision);
        RequireMatchId(matchId);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var stored = await ReadStoredAsync(
                connection, transaction, matchId, forUpdate: true, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Tournament match {matchId:D} was not found.");
            EnsureRevision(stored, expectedRevision);
            Validate(stored.Configuration, snapshot);
            var revision = checked(expectedRevision + 1);
            await UpdateSnapshotAsync(
                connection, transaction, matchId, expectedRevision, snapshot, revision, token)
                .ConfigureAwait(false);
            return new TournamentStoredMatch(stored.Configuration, snapshot, revision);
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<TournamentStoredMatch> DeactivateAsync(
        Guid matchId,
        long expectedRevision,
        TournamentRecoverySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        RequireRevision(expectedRevision);
        RequireMatchId(matchId);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var active = await ReadActiveAsync(connection, transaction, forUpdate: true, token)
                .ConfigureAwait(false);
            if (active != matchId)
                throw new InvalidOperationException(
                    $"Tournament match {matchId:D} is not the active match.");

            var stored = await ReadStoredAsync(
                connection, transaction, matchId, forUpdate: true, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Tournament match {matchId:D} was not found.");
            EnsureRevision(stored, expectedRevision);
            Validate(stored.Configuration, snapshot);
            var revision = checked(expectedRevision + 1);
            await UpdateSnapshotAsync(
                connection, transaction, matchId, expectedRevision, snapshot, revision, token)
                .ConfigureAwait(false);

            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = """
                UPDATE ano_tournament_runtime
                SET active_match_id = NULL
                WHERE singleton_id = 1 AND active_match_id = @match
                """;
            Add(clear, "@match", matchId.ToString("D"));
            if (await clear.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Active tournament pointer changed unexpectedly.");

            return new TournamentStoredMatch(stored.Configuration, snapshot, revision);
        }, cancellationToken: cancellationToken);
    }

    private static async ValueTask<TournamentStoredMatch?> ReadStoredAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid matchId,
        bool forUpdate,
        CancellationToken token)
    {
        string teamAName;
        string teamATag;
        ulong teamACaptain;
        string teamBName;
        string teamBTag;
        ulong teamBCaptain;
        TournamentBestOf bestOf;
        bool knifeRound;
        bool overtimeEnabled;
        string snapshotJson;
        long revision;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT best_of, knife_round, overtime_enabled,
                    team_a_name, team_a_tag, team_a_captain,
                    team_b_name, team_b_tag, team_b_captain,
                    snapshot_json, revision
                FROM ano_tournament_matches
                WHERE match_id = @match
                """ + (forUpdate ? " FOR UPDATE" : string.Empty);
            Add(command, "@match", matchId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return null;

            bestOf = (TournamentBestOf)Convert.ToInt32(reader.GetValue(0));
            knifeRound = Convert.ToBoolean(reader.GetValue(1));
            overtimeEnabled = Convert.ToBoolean(reader.GetValue(2));
            teamAName = reader.GetString(3);
            teamATag = reader.GetString(4);
            teamACaptain = Convert.ToUInt64(reader.GetValue(5));
            teamBName = reader.GetString(6);
            teamBTag = reader.GetString(7);
            teamBCaptain = Convert.ToUInt64(reader.GetValue(8));
            snapshotJson = reader.GetString(9);
            revision = Convert.ToInt64(reader.GetValue(10));
        }

        var teamA = new List<PlayerId>();
        var teamB = new List<PlayerId>();
        await using (var members = connection.CreateCommand())
        {
            members.Transaction = transaction;
            members.CommandText = """
                SELECT team_slot, steam_id
                FROM ano_tournament_members
                WHERE match_id = @match
                ORDER BY team_slot, steam_id
                """;
            Add(members, "@match", matchId.ToString("D"));
            await using var reader = await members.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var target = Convert.ToInt32(reader.GetValue(0)) switch
                {
                    (int)TournamentTeamSlot.TeamA => teamA,
                    (int)TournamentTeamSlot.TeamB => teamB,
                    _ => throw new InvalidOperationException(
                        "Stored tournament roster contains an invalid team slot."),
                };
                target.Add(new PlayerId(Convert.ToUInt64(reader.GetValue(1))));
            }
        }

        if (revision < 1)
            throw new InvalidOperationException("Stored tournament revision must be positive.");

        var configuration = new TournamentMatchConfiguration(
            matchId,
            bestOf,
            new TournamentTeam(teamAName, teamATag, new PlayerId(teamACaptain), teamA),
            new TournamentTeam(teamBName, teamBTag, new PlayerId(teamBCaptain), teamB),
            knifeRound,
            overtimeEnabled);
        var snapshot = DeserializeSnapshot(matchId, snapshotJson);
        Validate(configuration, snapshot);
        return new TournamentStoredMatch(configuration, snapshot, revision);
    }

    private static async ValueTask UpsertMatchAsync(
        DbConnection connection,
        DbTransaction transaction,
        TournamentMatchConfiguration configuration,
        TournamentRecoverySnapshot snapshot,
        long revision,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_tournament_matches (
                match_id, best_of, knife_round, overtime_enabled,
                team_a_name, team_a_tag, team_a_captain,
                team_b_name, team_b_tag, team_b_captain,
                snapshot_json, revision, updated_at_utc)
            VALUES (
                @match, @bestOf, @knife, @overtime,
                @aName, @aTag, @aCaptain,
                @bName, @bTag, @bCaptain,
                @snapshot, @revision, @updated)
            ON DUPLICATE KEY UPDATE
                best_of = VALUES(best_of),
                knife_round = VALUES(knife_round),
                overtime_enabled = VALUES(overtime_enabled),
                team_a_name = VALUES(team_a_name),
                team_a_tag = VALUES(team_a_tag),
                team_a_captain = VALUES(team_a_captain),
                team_b_name = VALUES(team_b_name),
                team_b_tag = VALUES(team_b_tag),
                team_b_captain = VALUES(team_b_captain),
                snapshot_json = VALUES(snapshot_json),
                revision = VALUES(revision),
                updated_at_utc = VALUES(updated_at_utc)
            """;
        AddConfiguration(command, configuration);
        Add(command, "@snapshot", SerializeSnapshot(snapshot));
        Add(command, "@revision", revision);
        Add(command, "@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask ReplaceMembersAsync(
        DbConnection connection,
        DbTransaction transaction,
        TournamentMatchConfiguration configuration,
        CancellationToken token)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ano_tournament_members WHERE match_id = @match";
            Add(delete, "@match", configuration.MatchId.ToString("D"));
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        foreach (var (slot, team) in new[]
        {
            (TournamentTeamSlot.TeamA, configuration.TeamA),
            (TournamentTeamSlot.TeamB, configuration.TeamB),
        })
        {
            foreach (var member in team.Members)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO ano_tournament_members (match_id, team_slot, steam_id)
                    VALUES (@match, @slot, @player)
                    """;
                Add(insert, "@match", configuration.MatchId.ToString("D"));
                Add(insert, "@slot", (int)slot);
                Add(insert, "@player", member.SteamId64);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask UpdateSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid matchId,
        long expectedRevision,
        TournamentRecoverySnapshot snapshot,
        long revision,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_tournament_matches
            SET snapshot_json = @snapshot,
                revision = @revision,
                updated_at_utc = @updated
            WHERE match_id = @match AND revision = @expected
            """;
        Add(command, "@snapshot", SerializeSnapshot(snapshot));
        Add(command, "@revision", revision);
        Add(command, "@updated", DateTime.UtcNow);
        Add(command, "@match", matchId.ToString("D"));
        Add(command, "@expected", expectedRevision);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new TournamentConcurrencyException(matchId, expectedRevision, revision);
    }

    private static async ValueTask<long?> ReadRevisionAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid matchId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision FROM ano_tournament_matches
            WHERE match_id = @match
            FOR UPDATE
            """;
        Add(command, "@match", matchId.ToString("D"));
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async ValueTask LockRuntimeAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken token)
        => _ = await ReadActiveAsync(connection, transaction, forUpdate: true, token)
            .ConfigureAwait(false);

    private static async ValueTask<Guid?> ReadActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        bool forUpdate,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT active_match_id
            FROM ano_tournament_runtime
            WHERE singleton_id = 1
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
    }

    private static async ValueTask SetActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid matchId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_tournament_runtime
            SET active_match_id = @match
            WHERE singleton_id = 1
            """;
        Add(command, "@match", matchId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Tournament runtime singleton is missing.");
    }

    private static void EnsureRevision(TournamentStoredMatch stored, long expectedRevision)
    {
        if (stored.Revision != expectedRevision)
            throw new TournamentConcurrencyException(
                stored.Configuration.MatchId, expectedRevision, stored.Revision);
    }

    private static void Validate(
        TournamentMatchConfiguration configuration,
        TournamentRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = TournamentMatchStateMachine.Restore(configuration, snapshot);
    }

    private static string SerializeSnapshot(TournamentRecoverySnapshot snapshot)
        => JsonSerializer.Serialize(new SnapshotDto(
            (int)snapshot.State,
            snapshot.Maps.ToArray(),
            snapshot.CurrentMapIndex,
            snapshot.TeamAMaps,
            snapshot.TeamBMaps,
            snapshot.ReadyPlayers.Select(x => x.SteamId64).ToArray(),
            snapshot.KnifeWinner is null ? null : (int)snapshot.KnifeWinner.Value,
            snapshot.SideChooser is null ? null : (int)snapshot.SideChooser.Value,
            (int)snapshot.TeamASide,
            (int)snapshot.TeamBSide,
            snapshot.ResumeState is null ? null : (int)snapshot.ResumeState.Value));

    private static TournamentRecoverySnapshot DeserializeSnapshot(Guid matchId, string json)
    {
        SnapshotDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<SnapshotDto>(json)
                ?? throw new InvalidOperationException("Stored tournament snapshot is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored tournament snapshot JSON is invalid.", exception);
        }

        return new TournamentRecoverySnapshot(
            matchId,
            (TournamentMatchState)dto.State,
            dto.Maps ?? [],
            dto.CurrentMapIndex,
            dto.TeamAMaps,
            dto.TeamBMaps,
            (dto.ReadyPlayers ?? []).Select(x => new PlayerId(x)).ToArray(),
            dto.KnifeWinner is null ? null : (TournamentTeamSlot)dto.KnifeWinner.Value,
            dto.SideChooser is null ? null : (TournamentTeamSlot)dto.SideChooser.Value,
            (PlayerTeam)dto.TeamASide,
            (PlayerTeam)dto.TeamBSide,
            dto.ResumeState is null ? null : (TournamentMatchState)dto.ResumeState.Value);
    }

    private static void AddConfiguration(
        DbCommand command,
        TournamentMatchConfiguration configuration)
    {
        Add(command, "@match", configuration.MatchId.ToString("D"));
        Add(command, "@bestOf", (int)configuration.BestOf);
        Add(command, "@knife", configuration.KnifeRound ? 1 : 0);
        Add(command, "@overtime", configuration.OvertimeEnabled ? 1 : 0);
        Add(command, "@aName", configuration.TeamA.Name);
        Add(command, "@aTag", configuration.TeamA.Tag);
        Add(command, "@aCaptain", configuration.TeamA.Captain.SteamId64);
        Add(command, "@bName", configuration.TeamB.Name);
        Add(command, "@bTag", configuration.TeamB.Tag);
        Add(command, "@bCaptain", configuration.TeamB.Captain.SteamId64);
    }

    private static void RequireMatchId(Guid matchId)
    {
        if (matchId == Guid.Empty)
            throw new ArgumentException("A match id is required.", nameof(matchId));
    }

    private static void RequireRevision(long revision)
    {
        if (revision < 1)
            throw new ArgumentOutOfRangeException(nameof(revision));
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record SnapshotDto(
        int State,
        string[]? Maps,
        int CurrentMapIndex,
        int TeamAMaps,
        int TeamBMaps,
        ulong[]? ReadyPlayers,
        int? KnifeWinner,
        int? SideChooser,
        int TeamASide,
        int TeamBSide,
        int? ResumeState);
}
