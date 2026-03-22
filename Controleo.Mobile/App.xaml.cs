namespace Controleo.Mobile;

public partial class App : Application
{
	private readonly MainPage _registerPage;
	private readonly ExpensesPage _expensesPage;
	private readonly DashboardPage _dashboardPage;
	private readonly BudgetsPage _budgetsPage;
	private readonly SettingsPage _settingsPage;

	public App(
		MainPage registerPage,
		ExpensesPage expensesPage,
		DashboardPage dashboardPage,
		BudgetsPage budgetsPage,
		SettingsPage settingsPage)
	{
		InitializeComponent();
		_registerPage = registerPage;
		_expensesPage = expensesPage;
		_dashboardPage = dashboardPage;
		_budgetsPage = budgetsPage;
		_settingsPage = settingsPage;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var tabs = new TabbedPage
		{
			Children =
			{
				new NavigationPage(_registerPage) { Title = "Registrar" },
				new NavigationPage(_expensesPage) { Title = "Gastos" },
				new NavigationPage(_dashboardPage) { Title = "Dashboard" },
				new NavigationPage(_budgetsPage) { Title = "Presupuestos" },
				new NavigationPage(_settingsPage) { Title = "Configuración" }
			}
		};

		return new Window(tabs);
	}
}