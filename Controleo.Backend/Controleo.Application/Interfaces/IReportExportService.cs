using Controleo.Application.DTOs;

namespace Controleo.Application.Interfaces;

public interface IReportExportService
{
    Task<ReportPreviewResponse> BuildPreviewAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<ReportExportFile> BuildCsvAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<ReportExportFile> BuildPdfAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct);
}
