using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;

namespace Retail360.Infrastructure;

public static class DependencyInjection
{
    private const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton(new NpgsqlConnectionFactory(connectionString));
        services.AddSingleton<PostgresTransaction>();
        services.AddSingleton<SqlMigrationRunner>();
        services.AddSingleton<ShopRoleQuery>();

        return services;
    }
}
