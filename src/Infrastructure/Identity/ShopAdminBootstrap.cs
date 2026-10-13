using Microsoft.Extensions.Configuration;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;
using Retail360.Domain.Identity;

namespace Retail360.Infrastructure.Identity;

public sealed class ShopAdminBootstrap
{
    private readonly IConfiguration _configuration;
    private readonly IShopUserDirectory _users;
    private readonly IShopPasswordHasher _passwords;
    private readonly IClock _clock;

    public ShopAdminBootstrap(
        IConfiguration configuration,
        IShopUserDirectory users,
        IShopPasswordHasher passwords,
        IClock clock)
    {
        _configuration = configuration;
        _users = users;
        _passwords = passwords;
        _clock = clock;
    }

    public async Task EnsureAdminAsync(CancellationToken cancellationToken)
    {
        var userName = _configuration["Authentication:Bootstrap:UserName"];
        var password = _configuration["Authentication:Bootstrap:Password"];
        var hasUserName = !string.IsNullOrWhiteSpace(userName);
        var hasPassword = !string.IsNullOrEmpty(password);
        if (!hasUserName && !hasPassword)
        {
            return;
        }

        if (!hasUserName || !hasPassword || userName!.Trim().Length > 256 || password!.Length < 8)
        {
            throw new InvalidOperationException(
                "Bootstrap admin requires Authentication:Bootstrap:UserName and a password of at least 8 characters.");
        }

        await _users.TryCreateAdminAsync(
            new NewShopUser(Guid.NewGuid().ToString("D"), userName.Trim(), _passwords.Hash(password), ShopRoleNames.Admin),
            _clock.UtcNow,
            cancellationToken);
    }
}
