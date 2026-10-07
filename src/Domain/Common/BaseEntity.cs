namespace Retail360.Domain.Common;

public abstract class BaseEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}
