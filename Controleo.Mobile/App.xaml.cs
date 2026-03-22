namespace Controleo.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _serviceProvider;

	public App(IServiceProvider serviceProvider)
	{
		InitializeComponent();
		_serviceProvider = serviceProvider;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var registerPage = _serviceProvider.GetRequiredService<MainPage>();
		var expensesPage = _serviceProvider.GetRequiredService<ExpensesPage>();
		var dashboardPage = _serviceProvider.GetRequiredService<DashboardPage>();
		var budgetsPage = _serviceProvider.GetRequiredService<BudgetsPage>();
		var settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();

		var tabs = new TabbedPage
		{
			Children =
			{
				new NavigationPage(registerPage) { Title = string.Empty, IconImageSource = "tab_home.svg" },
				new NavigationPage(expensesPage) { Title = string.Empty, IconImageSource = "tab_expenses.svg" },
				new NavigationPage(dashboardPage) { Title = string.Empty, IconImageSource = "tab_dashboard.svg" },
				new NavigationPage(budgetsPage) { Title = string.Empty, IconImageSource = "tab_budgets.svg" },
				new NavigationPage(settingsPage) { Title = string.Empty, IconImageSource = "tab_settings.svg" }
			}
		};

		return new Window(tabs);
	}
}