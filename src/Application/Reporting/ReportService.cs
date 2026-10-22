using Retail360.Domain.Reporting;

namespace Retail360.Application.Reporting;

public sealed class ReportService
{
    private readonly IReportStore _reports;

    public ReportService(IReportStore reports)
    {
        _reports = reports;
    }

    public Task<DashboardResponse> DashboardAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ReportText.RequireRange(from, to);
        return _reports.DashboardAsync(from, to, cancellationToken);
    }

    public Task<LowStockReportResponse> LowStockAsync(int? limit, CancellationToken cancellationToken)
    {
        return _reports.LowStockAsync(ReportText.RequireLowStockLimit(limit), cancellationToken);
    }
}
