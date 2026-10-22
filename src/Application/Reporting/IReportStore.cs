namespace Retail360.Application.Reporting;

public interface IReportStore
{
    Task<DashboardResponse> DashboardAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<LowStockReportResponse> LowStockAsync(int limit, CancellationToken cancellationToken);
}
