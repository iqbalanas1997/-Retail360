using System.Text.Json;
using Dapper;
using Npgsql;
using Retail360.Application.Audit;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Audit;

public sealed class AuditLogStore : IAuditLogStore
{
    private readonly NpgsqlConnectionFactory _connections;

    public AuditLogStore(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<AuditEntryResponse>> ListAsync(
        DateTime? fromUtc,
        DateTime? toExclusiveUtc,
        string? actorUserId,
        string? action,
        string? entityType,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
            """
            select
                a.id,
                a.occurred_at,
                a.actor_user_id,
                u.user_name as actor_user_name,
                a.action,
                a.entity_type,
                a.entity_id,
                a.details::text as details
            from audit_entries a
            left join asp_net_users u on u.id = a.actor_user_id
            where (cast(@FromUtc as timestamptz) is null or a.occurred_at >= cast(@FromUtc as timestamptz))
              and (cast(@ToExclusiveUtc as timestamptz) is null or a.occurred_at < cast(@ToExclusiveUtc as timestamptz))
              and (cast(@ActorUserId as text) is null or a.actor_user_id = cast(@ActorUserId as text))
              and (cast(@Action as text) is null or a.action = cast(@Action as text))
              and (cast(@EntityType as text) is null or a.entity_type = cast(@EntityType as text))
            order by a.occurred_at desc, a.id desc
            limit @Limit offset @Offset;
            """,
            new
            {
                FromUtc = fromUtc,
                ToExclusiveUtc = toExclusiveUtc,
                ActorUserId = actorUserId,
                Action = action,
                EntityType = entityType,
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken));
        return rows.Select(ToResponse).ToArray();
    }

    private static AuditEntryResponse ToResponse(AuditRow row)
    {
        return new AuditEntryResponse(
            row.Id,
            row.ActorUserId,
            row.ActorUserName,
            row.Action,
            row.EntityType,
            row.EntityId,
            row.OccurredAt,
            JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(row.Details) ? "{}" : row.Details));
    }

    private sealed class AuditRow
    {
        public Guid Id { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public string? ActorUserId { get; set; }

        public string? ActorUserName { get; set; }

        public string Action { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public string Details { get; set; } = "{}";
    }
}
