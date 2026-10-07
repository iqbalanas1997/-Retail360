namespace Retail360.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
