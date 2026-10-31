using System.Text.Json;

namespace Retail360.Application.Audit;

public sealed record AuditEntryResponse(
    Guid Id,
    string? ActorUserId,
    string? ActorUserName,
    string Action,
    string EntityType,
    string EntityId,
    DateTimeOffset OccurredAt,
    JsonElement Details);
