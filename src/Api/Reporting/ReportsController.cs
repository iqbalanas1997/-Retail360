using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Reporting;
using Retail360.Domain.Reporting;

namespace Retail360.Api.Reporting;

[ApiController]
[Authorize(Policy = ShopPolicies.ReportRead)]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardResponse>> Dashboard(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (from is null || to is null)
        {
            throw new ReportRuleException(ReportRuleFailure.Invalid, "Start date and end date are required.");
        }

        return Ok(await _reports.DashboardAsync(from.Value, to.Value, cancellationToken));
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<LowStockReportResponse>> LowStock(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await _reports.LowStockAsync(limit, cancellationToken));
    }
}
