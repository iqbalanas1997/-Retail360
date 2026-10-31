namespace Retail360.Application.Identity;

public sealed record SaveUserCommand(string? UserName, string? Role, string? Password);

public sealed record UserResponse(
    string Id,
    string UserName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
