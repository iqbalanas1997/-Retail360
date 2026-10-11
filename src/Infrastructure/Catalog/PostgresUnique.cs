using Npgsql;
using Retail360.Domain.Catalog;

namespace Retail360.Infrastructure.Catalog;

internal static class PostgresUnique
{
    public static CatalogRuleException? AsConflict(Exception exception, string message)
    {
        return Find(exception) is null
            ? null
            : new CatalogRuleException(CatalogRuleFailure.Conflict, message);
    }

    private static PostgresException? Find(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return postgres;
            }

            if (current is not AggregateException aggregate)
            {
                continue;
            }

            foreach (var inner in aggregate.InnerExceptions)
            {
                var found = Find(inner);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
