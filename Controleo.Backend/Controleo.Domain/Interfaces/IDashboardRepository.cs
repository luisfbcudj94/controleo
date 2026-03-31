using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface IDashboardRepository
{
    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken ct);
}
