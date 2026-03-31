using Controleo.Domain.Entities;
namespace Controleo.Application.Interfaces;
public interface IDashboardService
{
    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken ct);
}
