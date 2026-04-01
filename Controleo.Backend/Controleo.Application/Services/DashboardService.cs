using Controleo.Application.Interfaces;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class DashboardService(IDashboardRepository repo) : IDashboardService
{
    public Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string mk, CancellationToken ct) => repo.GetDashboardByCategoryAsync(userId, mk, ct);
    public Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string userId, string mk, CancellationToken ct) => repo.GetDashboardByPaymentMethodAsync(userId, mk, ct);
}
