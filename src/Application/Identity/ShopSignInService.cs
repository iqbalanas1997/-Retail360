using Retail360.Application.Catalog;

namespace Retail360.Application.Identity;

public sealed class ShopSignInService
{
    public const string InvalidCredentialsMessage = "The user name or password is incorrect.";

    private readonly IShopUserDirectory _users;
    private readonly IShopPasswordHasher _passwords;
    private readonly IAccessTokenIssuer _tokens;
    private readonly IClock _clock;
    private readonly ShopLockoutPolicy _lockout;
    private readonly string _unknownUserHash;

    public ShopSignInService(
        IShopUserDirectory users,
        IShopPasswordHasher passwords,
        IAccessTokenIssuer tokens,
        IClock clock,
        ShopLockoutPolicy lockout)
    {
        _users = users;
        _passwords = passwords;
        _tokens = tokens;
        _clock = clock;
        _lockout = lockout;
        _unknownUserHash = passwords.Hash("shop-sign-in-timing-placeholder");
    }

    public async Task<LoginResponse?> SignInAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var userName = command.UserName?.Trim();
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(command.Password) || userName.Length > 256)
        {
            return null;
        }

        var account = await _users.FindByUserNameAsync(userName.ToUpperInvariant(), cancellationToken);
        if (account is null || account.IsDeleted || string.IsNullOrEmpty(account.PasswordHash))
        {
            _passwords.Verify(_unknownUserHash, command.Password);
            return null;
        }

        var now = _clock.UtcNow;
        if (!_passwords.Verify(account.PasswordHash, command.Password))
        {
            await _users.RecordFailedAccessAsync(
                account.Id,
                _lockout.MaxFailedAttempts,
                now,
                now.Add(_lockout.Duration),
                cancellationToken);
            return null;
        }

        if (!await _users.ResetAccessFailuresIfUnlockedAsync(account.Id, now, cancellationToken))
        {
            return null;
        }

        var roles = await _users.ListRoleNamesAsync(account.Id, cancellationToken);
        var issued = _tokens.Issue(account.Id, account.UserName, roles, _clock.UtcNow);
        return new LoginResponse(issued.AccessToken, "Bearer", issued.ExpiresAt, account.Id, account.UserName);
    }
}
