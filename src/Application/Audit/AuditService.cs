using Retail360.Domain.Audit;

namespace Retail360.Application.Audit;

public sealed class AuditService
{
    private readonly IAuditLogStore _store;

    public AuditService(IAuditLogStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<AuditEntryResponse>> ListAsync(
        DateOnly? from,
        DateOnly? to,
        string? actorUserId,
        string? action,
        string? entityType,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        AuditText.RequireRange(from, to);
        return _store.ListAsync(
            ToUtc(from),
            to is null ? null : ToUtc(to.Value.AddDays(1)),
            AuditText.OptionalActor(actorUserId),
            AuditText.OptionalAction(action),
            AuditText.OptionalEntityType(entityType),
            AuditText.RequireLimit(limit),
            AuditText.RequireOffset(offset),
            cancellationToken);
    }

    private static DateTime? ToUtc(DateOnly? day)
    {
        return day is null ? null : day.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}
