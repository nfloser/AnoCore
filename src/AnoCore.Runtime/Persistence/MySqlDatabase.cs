using System.Data;
using System.Data.Common;
using AnoCore.Abstractions.Persistence;
using MySqlConnector;

namespace AnoCore.Runtime.Persistence;

public sealed class MySqlDatabase : IDatabase
{
    private readonly string _connectionString;

    public MySqlDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A database connection string is required.", nameof(connectionString));
        }

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
        };
        _connectionString = builder.ConnectionString;
    }

    public async ValueTask<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        catch (MySqlException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async ValueTask<TResult> WithConnectionAsync<TResult>(
        Func<DbConnection, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await action(connection, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TResult> InTransactionAsync<TResult>(
        Func<DbConnection, DbTransaction, CancellationToken, ValueTask<TResult>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var result = await action(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception actionException)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The database operation failed and its transaction could not be rolled back cleanly.",
                    actionException,
                    rollbackException);
            }

            throw;
        }
    }
}
