using Controleo.Domain.Interfaces; using Controleo.Infrastructure.Auth; using Controleo.Infrastructure.Options; using Controleo.Infrastructure.Persistence; using Microsoft.Extensions.Configuration; using Microsoft.Extensions.DependencyInjection;
namespace Controleo.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CosmosStorageOptions>(configuration.GetSection(CosmosStorageOptions.SectionName));
        services.PostConfigure<CosmosStorageOptions>(o => { o.MovementTypes = Norm(o.MovementTypes, DefMT()); o.PaymentMethods = Norm(o.PaymentMethods, DefPM()); });
        services.Configure<LocalAuthOptions>(configuration.GetSection(LocalAuthOptions.SectionName));
        services.AddSingleton<CosmosContainerProvider>();
        services.AddSingleton<IExpenseRepository, CosmosExpenseRepository>();
        services.AddSingleton<ICatalogRepository, CosmosCatalogRepository>();
        services.AddSingleton<IBudgetRepository, CosmosBudgetRepository>();
        services.AddSingleton<IRecurringExpenseRepository, CosmosRecurringExpenseRepository>();
        services.AddSingleton<IObligationRepository, CosmosObligationRepository>();
        services.AddSingleton<IDashboardRepository, CosmosDashboardRepository>();
        services.AddSingleton<IAdminUserRepository, CosmosAdminUserRepository>();
        services.AddSingleton<IUserAuthService, LocalUserAuthService>();
        services.AddSingleton<IAccessTokenValidator, JwtAccessTokenValidator>();
        return services;
    }
    private static string[] Norm(string[]? v, string[] fb) => (v is { Length: > 0 } ? v : fb).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] DefMT() => ["Basicos para vivir","Hogar","Salidas","Imprevistos","Suscripciones","Deudas","-","Prestamo","Bienestar","Viajes","Sogamoso obra"];
    private static string[] DefPM() => ["TC Black","TC Rappi","TD Bancolombia","TC Nu","Efectivo","Transferencia","-","Nequi"];
}
