using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public sealed class CategoryService
{
    private readonly ICategoryStore _categories;
    private readonly IClock _clock;

    public CategoryService(ICategoryStore categories, IClock clock)
    {
        _categories = categories;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NamedCatalogResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var categories = await _categories.ListAsync(cancellationToken);
        return categories.Select(ToResponse).ToArray();
    }

    public async Task<NamedCatalogResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await RequireAsync(id, cancellationToken);
        return ToResponse(category);
    }

    public async Task<NamedCatalogResponse> CreateAsync(SaveNameCommand command, CancellationToken cancellationToken)
    {
        var category = Category.Create(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(category.Name, category.Id, cancellationToken);
        await _categories.AddAsync(category, cancellationToken);
        return ToResponse(category);
    }

    public async Task<NamedCatalogResponse> UpdateAsync(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        var category = await RequireAsync(id, cancellationToken);
        category.Rename(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(category.Name, category.Id, cancellationToken);
        await _categories.UpdateAsync(category, cancellationToken);
        return ToResponse(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await RequireAsync(id, cancellationToken);
        if (await _categories.IsReferencedByActiveProductAsync(id, cancellationToken))
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "Category is still used by a product.");
        }

        category.SoftDelete(_clock.UtcNow);
        await _categories.UpdateAsync(category, cancellationToken);
    }

    private async Task<Category> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _categories.FindAsync(id, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Category was not found.");
    }

    private async Task EnsureNameAvailable(string name, Guid currentId, CancellationToken cancellationToken)
    {
        var existing = await _categories.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != currentId)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "A category with this name already exists.");
        }
    }

    private static NamedCatalogResponse ToResponse(Category category)
    {
        return new NamedCatalogResponse(category.Id, category.Name, category.CreatedAt, category.UpdatedAt);
    }
}
