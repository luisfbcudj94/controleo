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
				CreateTabPage(recurringPage, "tab_recurring.svg"),
				CreateTabPage(dashboardPage, "tab_dashboard.svg"),
				CreateTabPage(budgetsPage, "tab_budgets.svg"),
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
		tabs.BarBackgroundColor = GetColor("AppBg", "#0A0A0A");
		tabs.SelectedTabColor = GetColor("Primary", "#C8F55A");
		tabs.UnselectedTabColor = GetColor("AppHint", "#7B7B7B");
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

		Current.UserAppTheme = AppTheme.Dark;
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

		Current.UserAppTheme = AppTheme.Dark;

		Current.Resources["AppBg"] = Color.FromArgb("#0A0A0A");
		Current.Resources["AppSurface"] = Color.FromArgb("#141414");
		Current.Resources["AppSurface2"] = Color.FromArgb("#1D1D1D");
		Current.Resources["AppBorder"] = Color.FromArgb("#2A2A2A");
		Current.Resources["AppText"] = Color.FromArgb("#F2EFE9");
		Current.Resources["AppHint"] = Color.FromArgb("#B4B4B4");
		Current.Resources["Primary"] = Color.FromArgb("#C8F55A");
		Current.Resources["PrimaryDark"] = Color.FromArgb("#D8FF78");
		Current.Resources["PrimaryDarkText"] = Color.FromArgb("#242424");

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