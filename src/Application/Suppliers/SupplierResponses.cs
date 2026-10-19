namespace Retail360.Application.Suppliers;

public sealed record SaveSupplierCommand(
    string? Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes);

public sealed record SupplierResponse(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
