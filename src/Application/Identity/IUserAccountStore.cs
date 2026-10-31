namespace Retail360.Application.Identity;

public interface IUserAccountStore
{
    Task<IReadOnlyList<UserResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<UserResponse?> FindAsync(string id, CancellationToken cancellationToken);

    Task<UserResponse> AddAsync(
        string id,
        string userName,
        string role,
        string passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<UserResponse> UpdateAsync(
        string actorId,
        string id,
        string userName,
        string role,
        string? passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<UserResponse> SetActiveAsync(
        string actorId,
        string id,
        bool active,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task RemoveAsync(string actorId, string id, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task ReplacePasswordAsync(
        string userId,
        string passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
