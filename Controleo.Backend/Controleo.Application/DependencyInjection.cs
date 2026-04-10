using Controleo.Application.Interfaces;
using Controleo.Application.Services;
using Microsoft.Extensions.DependencyInjection;
namespace Controleo.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IExpenseService, ExpenseService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<IRecurringExpenseService, RecurringExpenseService>();
        services.AddSingleton<IObligationService, ObligationService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IAdminUserService, AdminUserService>();
        return services;
    }
}
