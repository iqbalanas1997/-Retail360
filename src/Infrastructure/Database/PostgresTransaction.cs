using Npgsql;

namespace Retail360.Infrastructure.Database;

public sealed class PostgresTransaction
{
    private readonly NpgsqlConnectionFactory _connections;

    public PostgresTransaction(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public Task ExecuteAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(async (connection, transaction, token) =>
        {
            await work(connection, transaction, token);
            return null;
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception error)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "The PostgreSQL transaction could not be rolled back.",
                    error,
                    rollbackError);
            }

            throw;
        }
    }
}
