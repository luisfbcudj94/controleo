namespace Controleo.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _serviceProvider;

	public App(IServiceProvider serviceProvider)
	{
		InitializeComponent();
		_serviceProvider = serviceProvider;
		ApplySavedTheme();
		ApplyThemePalette();
		RequestedThemeChanged += (_, __) => ApplyThemePalette();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
		NavigationPage.SetHasNavigationBar(loginPage, false);
		return new Window(new NavigationPage(loginPage));
	}

	public void ShowMainApp()
	{
		var registerPage = _serviceProvider.GetRequiredService<MainPage>();
		var expensesPage = _serviceProvider.GetRequiredService<ExpensesPage>();
		var recurringPage = _serviceProvider.GetRequiredService<RecurringPage>();
		var dashboardPage = _serviceProvider.GetRequiredService<DashboardPage>();
		var budgetsPage = _serviceProvider.GetRequiredService<BudgetsPage>();
		var settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();

		var tabs = new TabbedPage
		{
			Children =
			{
				CreateTabPage(registerPage, "tab_home.svg"),
				CreateTabPage(expensesPage, "tab_expenses.svg"),
				CreateTabPage(dashboardPage, "tab_dashboard.svg"),
				CreateTabPage(budgetsPage, "tab_budgets.svg"),
				CreateTabPage(recurringPage, "tab_recurring.svg"),
				CreateTabPage(settingsPage, "tab_settings.svg")
			}
		};

		ApplyTabColors(tabs);
		RequestedThemeChanged += (_, __) => ApplyTabColors(tabs);

		if (Current?.Windows.FirstOrDefault() is { } activeWindow)
		{
			activeWindow.Page = tabs;
		}
	}

	private static void ApplyTabColors(TabbedPage tabs)
	{
		tabs.BarBackgroundColor = GetColor("White", "#FFFFFF");
		tabs.SelectedTabColor = GetColor("Primary", "#2D6A4F");
		tabs.UnselectedTabColor = GetColor("AppHint", "#7A9183");
		tabs.BarTextColor = tabs.UnselectedTabColor;
	}

	private static NavigationPage CreateTabPage(Page page, string icon)
	{
		NavigationPage.SetHasNavigationBar(page, false);
		return new NavigationPage(page)
		{
			Title = string.Empty,
			IconImageSource = icon
		};
	}

	private static void ApplySavedTheme()
	{
		if (Current is null)
		{
			return;
		}

		Current.UserAppTheme = AppTheme.Light;
	}

	public static void RefreshThemePalette()
	{
		ApplyThemePalette();
	}

	private static void ApplyThemePalette()
	{
		if (Current?.Resources is null)
		{
			return;
		}

		Current.UserAppTheme = AppTheme.Light;

		Current.Resources["AppBg"] = Color.FromArgb("#F4F7F5");
		Current.Resources["AppSurface"] = Color.FromArgb("#FFFFFF");
		Current.Resources["AppSurface2"] = Color.FromArgb("#EFF3F1");
		Current.Resources["AppBorder"] = Color.FromArgb("#DCE5DF");
		Current.Resources["AppText"] = Color.FromArgb("#1A2E23");
		Current.Resources["AppHint"] = Color.FromArgb("#7A9183");
		Current.Resources["Primary"] = Color.FromArgb("#2D6A4F");
		Current.Resources["PrimaryDark"] = Color.FromArgb("#1B4332");
		Current.Resources["PrimaryDarkText"] = Color.FromArgb("#FFFFFF");

		if (Current.Windows.FirstOrDefault()?.Page is TabbedPage tabs)
		{
			ApplyTabColors(tabs);
		}
	}

	private static Color GetColor(string resourceKey, string fallbackHex)
	{
		if (Current?.Resources?.TryGetValue(resourceKey, out var resource) == true && resource is Color color)
		{
			return color;
		}

		return Color.FromArgb(fallbackHex);
	}
}