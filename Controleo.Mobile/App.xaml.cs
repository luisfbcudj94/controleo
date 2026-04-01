using Microsoft.Maui.Controls.PlatformConfiguration;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Features.Auth;
using Controleo.Mobile.Features.Budgets;
using Controleo.Mobile.Features.Dashboard;
using Controleo.Mobile.Features.Expenses;
using Controleo.Mobile.Features.Recurring;
using Controleo.Mobile.Features.Register;
using Controleo.Mobile.Features.Settings;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _serviceProvider;
	private FlyoutPage? _mainFlyout;
	private TabbedPage? _mainTabs;
	private NavigationPage? _lastContentTab;
	private bool _isNavigatingFromMenu;

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
		var dashboardPage = _serviceProvider.GetRequiredService<DashboardPage>();

		var menuLauncherPage = CreateTabPage(CreateMenuLauncherPage(), "Menú", "tab_menu.svg");

		var tabs = new TabbedPage
		{
			Children =
			{
				CreateTabPage(registerPage, "Registro", "tab_home.svg"),
				CreateTabPage(expensesPage, "Gastos", "tab_expenses.svg"),
				CreateTabPage(dashboardPage, "Dashboard", "tab_dashboard.svg"),
				menuLauncherPage
			}
		};

		var sideMenuPage = new SideMenuPage();
		var flyout = new FlyoutPage
		{
			Flyout = sideMenuPage,
			Detail = tabs,
			FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover,
			IsPresented = false
		};

		_mainFlyout = flyout;
		_mainTabs = tabs;
		_lastContentTab = tabs.Children
			.OfType<NavigationPage>()
			.FirstOrDefault(page => page != menuLauncherPage);

		void OpenFlyoutAndRestoreTab()
		{
			if (_mainFlyout is not null)
			{
				_mainFlyout.IsPresented = true;
			}

			var fallbackTab = _lastContentTab
				?? tabs.Children.OfType<NavigationPage>().FirstOrDefault(page => page != menuLauncherPage);
			if (fallbackTab is not null)
			{
				tabs.Dispatcher.Dispatch(() => tabs.CurrentPage = fallbackTab);
			}
		}

		tabs.CurrentPageChanged += (_, __) =>
		{
			if (tabs.CurrentPage == menuLauncherPage)
			{
				OpenFlyoutAndRestoreTab();
				return;
			}

			if (tabs.CurrentPage is NavigationPage currentTab && currentTab != menuLauncherPage)
			{
				_lastContentTab = currentTab;
			}
		};

		flyout.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName != nameof(FlyoutPage.IsPresented))
			{
				return;
			}

			if (!flyout.IsPresented && tabs.CurrentPage == menuLauncherPage)
			{
				var fallbackTab = _lastContentTab
					?? tabs.Children.OfType<NavigationPage>().FirstOrDefault(page => page != menuLauncherPage);
				if (fallbackTab is not null)
				{
					tabs.Dispatcher.Dispatch(() => tabs.CurrentPage = fallbackTab);
				}
			}
		};

		sideMenuPage.DestinationSelected += async (_, destination) =>
		{
			if (_isNavigatingFromMenu)
			{
				return;
			}

			_isNavigatingFromMenu = true;
			try
			{
				if (_mainFlyout is not null)
				{
					_mainFlyout.IsPresented = false;
				}

				// Let the flyout close animation and tab restore settle
				await Task.Delay(250);

				await NavigateFromSideMenuAsync(destination);
			}
			finally
			{
				_isNavigatingFromMenu = false;
			}
		};

#if ANDROID
		Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetToolbarPlacement(
			tabs,
			Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);
#endif

		ApplyTabColors(tabs);
		RequestedThemeChanged += (_, __) => ApplyTabColors(tabs);

		if (Current?.Windows.FirstOrDefault() is { } activeWindow)
		{
			activeWindow.Page = flyout;
		}
	}

	private async Task NavigateFromSideMenuAsync(SideMenuDestination destination)
	{
		try
		{
			var apiClient = _serviceProvider.GetRequiredService<IExpenseApiClient>();
			var authService = _serviceProvider.GetRequiredService<IAuthService>();
			var monthContext = _serviceProvider.GetRequiredService<IMonthContextService>();
			var colorService = _serviceProvider.GetRequiredService<ICatalogColorService>();
			var paymentIconService = _serviceProvider.GetRequiredService<IPaymentIconService>();

			Page destinationPage = destination switch
			{
				SideMenuDestination.Profile => new ProfilePage(authService),
				SideMenuDestination.Budgets => new BudgetsPage(apiClient, monthContext, colorService),
				SideMenuDestination.PaymentMethods => new SettingsPage(apiClient, authService, colorService, paymentIconService, SettingsSectionMode.PaymentOnly),
				SideMenuDestination.MovementTypes => new SettingsPage(apiClient, authService, colorService, paymentIconService, SettingsSectionMode.MovementOnly),
				SideMenuDestination.Recurring => new RecurringPage(apiClient, colorService, paymentIconService),
				_ => new ProfilePage(authService)
			};

			// Use modal navigation — completely independent of tab stacks, no orphan/crash risk
			var modalNav = new NavigationPage(destinationPage);
			NavigationPage.SetHasNavigationBar(destinationPage, true);
			destinationPage.Title = destination switch
			{
				SideMenuDestination.Profile => "Perfil",
				SideMenuDestination.Budgets => "Presupuestos",
				SideMenuDestination.PaymentMethods => "Medios de pago",
				SideMenuDestination.MovementTypes => "Tipos de gasto",
				SideMenuDestination.Recurring => "Gastos recurrentes",
				_ => ""
			};

			// Add close button so the user can dismiss the modal
			destinationPage.ToolbarItems.Add(new ToolbarItem
			{
				Text = "✕",
				Command = new Command(async () => await modalNav.Navigation.PopModalAsync())
			});

			var currentPage = _mainTabs?.CurrentPage ?? _mainFlyout?.Detail;
			if (currentPage is not null)
			{
				await currentPage.Navigation.PushModalAsync(modalNav);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SideMenu] Navigation error: {ex.Message}");
		}
	}

	private static void ApplyTabColors(TabbedPage tabs)
	{
		tabs.BarBackgroundColor = GetColor("White", "#FFFFFF");
		tabs.SelectedTabColor = GetColor("Primary", "#2D6A4F");
		tabs.UnselectedTabColor = GetColor("AppHint", "#7A9183");
		tabs.BarTextColor = tabs.UnselectedTabColor;
	}

	private static NavigationPage CreateTabPage(Page page, string title, string icon)
	{
		NavigationPage.SetHasNavigationBar(page, false);
		return new NavigationPage(page)
		{
			Title = title,
			IconImageSource = icon
		};
	}

	private static ContentPage CreateMenuLauncherPage()
	{
		return new ContentPage
		{
			BackgroundColor = GetColor("AppBg", "#F4F7F5"),
			Content = new Grid
			{
				Children =
				{
					new Label
					{
						Text = "Abre el menú lateral para ver opciones.",
						TextColor = GetColor("AppHint", "#7A9183"),
						HorizontalOptions = LayoutOptions.Center,
						VerticalOptions = LayoutOptions.Center,
						HorizontalTextAlignment = TextAlignment.Center,
						Margin = new Thickness(24)
					}
				}
			}
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

		var rootPage = Current.Windows.FirstOrDefault()?.Page;
		if (rootPage is FlyoutPage flyout && flyout.Detail is TabbedPage flyoutTabs)
		{
			ApplyTabColors(flyoutTabs);
		}
		else if (rootPage is TabbedPage tabs)
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