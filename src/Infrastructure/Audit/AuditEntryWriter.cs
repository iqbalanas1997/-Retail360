using System.Text.Json;
using Dapper;
using Npgsql;
using Retail360.Application.Audit;

namespace Retail360.Infrastructure.Audit;

internal static class AuditEntryWriter
{
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwordhash",
        "password_hash",
        "accesstoken",
        "access_token",
        "signingkey",
        "signing_key",
        "secret",
        "securitystamp",
        "security_stamp"
    };

    public static async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string action,
        string entityType,
        string entityId,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string?> details,
        string? actorUserId,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in details)
        {
            if (SecretKeys.Contains(pair.Key))
            {
                throw new InvalidOperationException("Audit details cannot include a secret.");
            }

            if (pair.Value is not null)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        var actor = string.IsNullOrWhiteSpace(actorUserId) ? AuditActor.UserId : actorUserId.Trim();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into audit_entries (
                id, occurred_at, actor_user_id, action, entity_type, entity_id, details)
            values (
                @Id, @OccurredAt, @ActorUserId, @Action, @EntityType, @EntityId, cast(@Details as jsonb));
            """,
            new
            {
                Id = Guid.NewGuid(),
                OccurredAt = occurredAt,
                ActorUserId = string.IsNullOrWhiteSpace(actor) ? null : actor,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = JsonSerializer.Serialize(payload)
            },
            transaction,
            cancellationToken: cancellationToken));
    }
}
