using Retail360.Application.Catalog;
using Retail360.Domain.Identity;

namespace Retail360.Application.Identity;

public sealed class UserService
{
    private readonly IUserAccountStore _users;
    private readonly IShopPasswordHasher _passwords;
    private readonly IClock _clock;

    public UserService(IUserAccountStore users, IShopPasswordHasher passwords, IClock clock)
    {
        _users = users;
        _passwords = passwords;
        _clock = clock;
    }

    public Task<IReadOnlyList<UserResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        return _users.ListAsync(
            name,
            activeOnly,
            UserText.RequireLimit(limit),
            UserText.RequireOffset(offset),
            cancellationToken);
    }

    public async Task<UserResponse> GetAsync(string id, CancellationToken cancellationToken)
    {
        return await _users.FindAsync(id, cancellationToken)
            ?? throw new UserRuleException(UserRuleFailure.NotFound, "User was not found.");
    }

    public Task<UserResponse> CreateAsync(SaveUserCommand command, CancellationToken cancellationToken)
    {
        var userName = UserText.RequireUserName(command.UserName);
        var role = UserText.RequireRole(command.Role);
        var password = UserText.RequirePassword(command.Password);
        return _users.AddAsync(
            Guid.NewGuid().ToString("D"),
            userName,
            role,
            _passwords.Hash(password),
            _clock.UtcNow,
            cancellationToken);
    }

    public Task<UserResponse> UpdateAsync(
        string actorId,
        string id,
        SaveUserCommand command,
        CancellationToken cancellationToken)
    {
        var userName = UserText.RequireUserName(command.UserName);
        var role = UserText.RequireRole(command.Role);
        var password = UserText.OptionalPassword(command.Password);
        return _users.UpdateAsync(
            actorId,
            id,
            userName,
            role,
            password is null ? null : _passwords.Hash(password),
            _clock.UtcNow,
            cancellationToken);
    }

    public Task<UserResponse> SetActiveAsync(
        string actorId,
        string id,
        bool active,
        CancellationToken cancellationToken)
    {
        return _users.SetActiveAsync(actorId, id, active, _clock.UtcNow, cancellationToken);
    }

    public Task RemoveAsync(string actorId, string id, CancellationToken cancellationToken)
    {
        return _users.RemoveAsync(actorId, id, _clock.UtcNow, cancellationToken);
    }
}
