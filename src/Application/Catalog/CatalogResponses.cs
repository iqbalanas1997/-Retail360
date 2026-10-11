namespace Retail360.Application.Catalog;

public sealed record NamedCatalogResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    Guid UnitId,
    string UnitName,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal MinimumStock,
    bool IsActive,
    IReadOnlyList<string> Barcodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveNameCommand(string Name);

public sealed record SaveProductCommand(
    string Name,
    Guid? CategoryId,
    Guid? BrandId,
    Guid UnitId,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal MinimumStock,
    IReadOnlyList<string>? Barcodes);
