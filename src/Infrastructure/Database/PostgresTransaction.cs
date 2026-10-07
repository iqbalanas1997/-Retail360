using Npgsql;

namespace Retail360.Infrastructure.Database;

public sealed class PostgresTransaction
{
    private readonly NpgsqlConnectionFactory _connections;

    public PostgresTransaction(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task ExecuteAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await work(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
