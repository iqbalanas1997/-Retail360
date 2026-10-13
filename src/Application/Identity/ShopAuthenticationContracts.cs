namespace Retail360.Application.Identity;

public sealed record LoginCommand(string? UserName, string? Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string UserId,
    string UserName);

public sealed record IssuedAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record ShopUserAccount(
    string Id,
    string UserName,
    string? PasswordHash,
    bool IsDeleted,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd);

public sealed record NewShopUser(string Id, string UserName, string PasswordHash, string RoleName);

public sealed record ShopLockoutPolicy(int MaxFailedAttempts, TimeSpan Duration)
{
    public static ShopLockoutPolicy Default { get; } = new(5, TimeSpan.FromMinutes(5));
}

public interface IShopUserDirectory
{
    Task<ShopUserAccount?> FindByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<ShopUserAccount?> FindActiveByIdAsync(string userId, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListRoleNamesAsync(string userId, CancellationToken cancellationToken);

    Task<bool> HasActiveAdminAsync(CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task CreateAsync(NewShopUser user, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task RecordFailedAccessAsync(
        string userId,
        int maxFailedAttempts,
        DateTimeOffset utcNow,
        DateTimeOffset lockoutEnd,
        CancellationToken cancellationToken);

    Task<bool> ResetAccessFailuresIfUnlockedAsync(
        string userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<bool> TryCreateAdminAsync(NewShopUser user, DateTimeOffset utcNow, CancellationToken cancellationToken);
}

public interface IShopPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(
        string userId,
        string userName,
        IReadOnlyList<string> roles,
        DateTimeOffset issuedAt);
}
