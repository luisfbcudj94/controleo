using Microsoft.Extensions.Logging;
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

		builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
		builder.Services.AddSingleton<AuthService>();
		builder.Services.AddSingleton<MonthContextService>();
		builder.Services.AddSingleton<ExpenseApiClient>();
		builder.Services.AddSingleton<LoginPage>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<ExpensesPage>();
		builder.Services.AddSingleton<RecurringPage>();
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddSingleton<BudgetsPage>();
		builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
