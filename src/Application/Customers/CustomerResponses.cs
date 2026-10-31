namespace Retail360.Application.Customers;

public sealed record SaveCustomerCommand(
    string? Name,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes);

public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
