namespace Controleo.Mobile.Features.Settings;

public enum SideMenuDestination
{
    Profile,
    Budgets,
    PaymentMethods,
    MovementTypes,
    Recurring,
    Reports,
    Obligations,
    SmartScore,
    Goals,
    MoneyCoach
}

public partial class SideMenuPage : ContentPage
{
    private bool _isConfigurationExpanded;

    public event EventHandler<SideMenuDestination>? DestinationSelected;

    public SideMenuPage()
    {
        InitializeComponent();
        _isConfigurationExpanded = false;
        ConfigItemsContainer.IsVisible = false;
        ConfigItemsContainer.Opacity = 0;
        ConfigChevronLabel.Text = "▸";
    }

    private void OnProfileTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Profile);

    private void OnBudgetsTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Budgets);

    private void OnPaymentMethodsTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.PaymentMethods);

    private void OnMovementTypesTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.MovementTypes);

    private void OnRecurringTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Recurring);

    private void OnReportsTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Reports);

    private void OnObligationsTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Obligations);

    private void OnSmartScoreTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.SmartScore);

    private void OnGoalsTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.Goals);

    private void OnMoneyCoachTapped(object? sender, TappedEventArgs e)
        => DestinationSelected?.Invoke(this, SideMenuDestination.MoneyCoach);

    private async void OnConfigurationTapped(object? sender, TappedEventArgs e)
    {
        _isConfigurationExpanded = !_isConfigurationExpanded;
        ConfigChevronLabel.Text = _isConfigurationExpanded ? "▾" : "▸";

        if (_isConfigurationExpanded)
        {
            ConfigItemsContainer.IsVisible = true;
            ConfigItemsContainer.Opacity = 0;
            await ConfigItemsContainer.FadeTo(1, 140, Easing.CubicOut);
            return;
        }

        await ConfigItemsContainer.FadeTo(0, 110, Easing.CubicIn);
        ConfigItemsContainer.IsVisible = false;
    }
}
