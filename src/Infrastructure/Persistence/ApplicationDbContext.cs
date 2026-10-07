using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Retail360.Domain.Common;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Identity;

namespace Retail360.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<
    ApplicationUser,
    ApplicationRole,
    string,
    IdentityUserClaim<string>,
    ApplicationUserRole,
    IdentityUserLogin<string>,
    IdentityRoleClaim<string>,
    IdentityUserToken<string>>
{
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyPersistenceRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyPersistenceRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
        modelBuilder.Entity<ApplicationUser>().HasQueryFilter(user => !user.IsDeleted);
        modelBuilder.Entity<ApplicationRole>().HasQueryFilter(role => !role.IsDeleted);
        SeedRoles(modelBuilder);
    }

    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var tableName = entityType.GetTableName();
            if (!string.IsNullOrEmpty(tableName))
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entityType.GetKeys())
            {
                var keyName = key.GetName();
                if (!string.IsNullOrEmpty(keyName))
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var constraintName = foreignKey.GetConstraintName();
                if (!string.IsNullOrEmpty(constraintName))
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (!string.IsNullOrEmpty(indexName))
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        UnicodeCategory? previousCategory = null;

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (current == '_')
            {
                builder.Append('_');
                previousCategory = null;
                continue;
            }

            var category = char.GetUnicodeCategory(current);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.TitlecaseLetter)
            {
                if (previousCategory == UnicodeCategory.SpaceSeparator
                    || previousCategory == UnicodeCategory.LowercaseLetter
                    || previousCategory is not null
                        and not UnicodeCategory.DecimalDigitNumber
                        && index + 1 < name.Length
                        && char.IsLower(name[index + 1]))
                {
                    builder.Append('_');
                }

                current = char.ToLowerInvariant(current);
            }

            builder.Append(current);
            previousCategory = category;
        }

        return builder.ToString();
    }

    private static void SeedRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationRole>().HasData(
            CreateRole("8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e01", ShopRoleNames.Admin),
            CreateRole("8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e02", ShopRoleNames.Manager),
            CreateRole("8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e03", ShopRoleNames.Cashier),
            CreateRole("8f4c2e1a-6b3d-4a91-9c70-1a2b3c4d5e04", ShopRoleNames.InventoryStaff));
    }

    private static ApplicationRole CreateRole(string id, string name)
    {
        return new ApplicationRole
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            ConcurrencyStamp = id,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
            IsDeleted = false
        };
    }

    private void ApplyPersistenceRules()
    {
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
        }

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Id == Guid.Empty)
            {
                entry.Entity.Id = Guid.NewGuid();
            }
        }

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
                entry.Entity.UpdatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
            }
        }
    }
}
