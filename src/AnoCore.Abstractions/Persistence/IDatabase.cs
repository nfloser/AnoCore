using System.Data;
using System.Data.Common;

namespace AnoCore.Abstractions.Persistence;

public interface IDatabase
{
    ValueTask<bool> PingAsync(CancellationToken cancellationToken = default);

    ValueTask<TResult> WithConnectionAsync<TResult>(
        Func<DbConnection, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> InTransactionAsync<TResult>(
        Func<DbConnection, DbTransaction, CancellationToken, ValueTask<TResult>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
