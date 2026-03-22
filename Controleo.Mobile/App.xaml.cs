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
				new NavigationPage(registerPage) { Title = "Registrar" },
				new NavigationPage(expensesPage) { Title = "Gastos" },
				new NavigationPage(dashboardPage) { Title = "Dashboard" },
				new NavigationPage(budgetsPage) { Title = "Presupuestos" },
				new NavigationPage(settingsPage) { Title = "Configuración" }
			}
		};

		return new Window(tabs);
	}
}