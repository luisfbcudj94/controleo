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
			});

		var apiBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
			? "http://10.0.2.2:5051/"
			: "http://localhost:5051/";

		// Configuración cloud (Cloud Run) para despliegues:
		// var apiBaseUrl = "https://controleo-api-414008451882.us-central1.run.app/";

		builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
		builder.Services.AddSingleton<ExpenseApiClient>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<ExpensesPage>();
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddSingleton<BudgetsPage>();
		builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
