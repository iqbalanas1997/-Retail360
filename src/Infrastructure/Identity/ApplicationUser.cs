using Microsoft.AspNetCore.Identity;
using Retail360.Domain.Common;

namespace Retail360.Infrastructure.Identity;

public class ApplicationUser : IdentityUser, IAuditable, ISoftDeletable
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}
