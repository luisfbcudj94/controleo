using Microsoft.Extensions.Logging;
using Controleo.Mobile.Interfaces;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				handlers.AddHandler<TabbedPage, Controleo.Mobile.Platforms.Android.CustomTabbedPageHandler>();
#endif
			});

		var apiBaseUrl = Environment.GetEnvironmentVariable("CONTROLEO_API_BASE_URL");
		if (string.IsNullOrWhiteSpace(apiBaseUrl))
		{
			apiBaseUrl = "https://controleo-api.azurewebsites.net/";
		}

		if (!apiBaseUrl.EndsWith('/'))
		{
			apiBaseUrl += "/";
		}

		// Core services — singleton, registered by interface
		builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<IMonthContextService, MonthContextService>();
		builder.Services.AddSingleton<ICatalogColorService, PastelColorHelper>();
		builder.Services.AddSingleton<IPaymentIconService, PaymentIconService>();
		builder.Services.AddSingleton<IExpenseApiClient, ExpenseApiClient>();

		// Pages — transient to avoid reuse/parent bugs on re-navigation
		builder.Services.AddSingleton<LoginPage>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<ExpensesPage>();
		builder.Services.AddTransient<RecurringPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<BudgetsPage>();
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
