using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Audit;

namespace Retail360.Api.Audit;

[ApiController]
[Authorize(Policy = ShopPolicies.AuditRead)]
[Route("api/audit")]
public sealed class AuditController : ControllerBase
{
    private readonly AuditService _audit;

    public AuditController(AuditService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditEntryResponse>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? actorUserId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _audit.ListAsync(
            from,
            to,
            actorUserId,
            action,
            entityType,
            limit,
            offset,
            cancellationToken));
    }
}
