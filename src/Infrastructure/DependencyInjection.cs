using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;
using Retail360.Application.Inventory;
using Retail360.Application.Suppliers;
using Retail360.Infrastructure.Catalog;
using Retail360.Infrastructure.Inventory;
using Retail360.Infrastructure.Suppliers;
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
        services.AddSingleton(ShopAuthenticationSettings.Read(configuration));
        services.AddSingleton(ShopAuthenticationSettings.ReadLockout(configuration));
        services.AddSingleton<IShopPasswordHasher, ShopPasswordHasher>();
        services.AddSingleton<IShopUserDirectory, ShopUserDirectory>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ShopAdminBootstrap>();
        services.AddSingleton<ICategoryStore, CategoryCatalogStore>();
        services.AddSingleton<IBrandStore, BrandCatalogStore>();
        services.AddSingleton<IUnitStore, UnitCatalogStore>();
        services.AddSingleton<IProductStore, ProductCatalogStore>();
        services.AddSingleton<IAreaStore, AreaInventoryStore>();
        services.AddSingleton<IShelfStore, ShelfInventoryStore>();
        services.AddSingleton<StockLedgerStore>();
        services.AddSingleton<IStockLedger>(provider => provider.GetRequiredService<StockLedgerStore>());
        services.AddSingleton<IInventoryProductLookup>(provider => provider.GetRequiredService<StockLedgerStore>());
        services.AddSingleton<ISupplierStore, SupplierStore>();

        return services;
    }
}
