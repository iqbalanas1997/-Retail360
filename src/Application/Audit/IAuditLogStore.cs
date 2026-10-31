namespace Retail360.Application.Audit;

public interface IAuditLogStore
{
    Task<IReadOnlyList<AuditEntryResponse>> ListAsync(
        DateTime? fromUtc,
        DateTime? toExclusiveUtc,
        string? actorUserId,
        string? action,
        string? entityType,
        int limit,
        int offset,
        CancellationToken cancellationToken);
}
