using Microsoft.AspNetCore.Identity;
using Retail360.Application.Identity;

namespace Retail360.Infrastructure.Identity;

public sealed class ShopPasswordHasher : IShopPasswordHasher
{
    private readonly PasswordHasher<ShopPasswordSubject> _hasher = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword(ShopPasswordSubject.Instance, password);
    }

    public bool Verify(string passwordHash, string password)
    {
        var result = _hasher.VerifyHashedPassword(ShopPasswordSubject.Instance, passwordHash, password);
        return result != PasswordVerificationResult.Failed;
    }

    private sealed class ShopPasswordSubject
    {
        public static readonly ShopPasswordSubject Instance = new();
    }
}
