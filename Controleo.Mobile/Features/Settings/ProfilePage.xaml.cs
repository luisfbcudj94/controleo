using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Features.Auth;
using System.Globalization;

namespace Controleo.Mobile.Features.Settings;

public partial class ProfilePage : ContentPage
{
    private readonly IAuthService _authService;
    private readonly IExpenseApiClient _apiClient;

    public ProfilePage(IAuthService authService, IExpenseApiClient apiClient)
    {
        InitializeComponent();
        _authService = authService;
        _apiClient = apiClient;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var name = _authService.CurrentUserName;
        NameLabel.Text = name;
        EmailLabel.Text = _authService.CurrentUserEmail;
        AvatarLabel.Text = string.IsNullOrWhiteSpace(name)
            ? "C"
            : name.Trim()[0].ToString().ToUpperInvariant();

        IncomeStatusLabel.Text = string.Empty;
        var sessionIncome = _authService.CurrentMonthlyIncome;
        MonthlyIncomeEntry.Text = sessionIncome.HasValue
            ? sessionIncome.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;

        var profile = await _apiClient.GetUserProfileAsync(CancellationToken.None);
        if (profile.IsSuccess)
        {
            MonthlyIncomeEntry.Text = profile.MonthlyIncome.HasValue
                ? profile.MonthlyIncome.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;

            _authService.UpdateCurrentMonthlyIncome(profile.MonthlyIncome);
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.Message))
        {
            IncomeStatusLabel.Text = profile.Message;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        await _authService.SignOutAsync();
        if (Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            var loginPage = new LoginPage(_authService);
            NavigationPage.SetHasNavigationBar(loginPage, false);
            window.Page = new NavigationPage(loginPage);
        }
    }

    private async void OnSaveIncomeClicked(object? sender, EventArgs e)
    {
        if (!TryParseMonthlyIncome(MonthlyIncomeEntry.Text, out var monthlyIncome, out var validationMessage))
        {
            IncomeStatusLabel.Text = validationMessage;
            return;
        }

        IncomeStatusLabel.Text = "Guardando...";
        var result = await _apiClient.UpdateMonthlyIncomeAsync(monthlyIncome, CancellationToken.None);
        if (result.IsSuccess)
        {
            _authService.UpdateCurrentMonthlyIncome(monthlyIncome);
            MonthlyIncomeEntry.Text = monthlyIncome.HasValue
                ? monthlyIncome.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;

            IncomeStatusLabel.Text = monthlyIncome.HasValue
                ? "Ingreso actualizado correctamente."
                : "Ingreso eliminado correctamente.";
            return;
        }

        IncomeStatusLabel.Text = result.Message;
    }

    private static bool TryParseMonthlyIncome(string? raw, out decimal? monthlyIncome, out string message)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            monthlyIncome = null;
            message = string.Empty;
            return true;
        }

        var normalized = raw.Trim();
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var parsed)
            && !decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
            && !decimal.TryParse(normalized.Replace(".", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
            && !decimal.TryParse(normalized.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            monthlyIncome = null;
            message = "Ingresa un valor numerico valido o deja el campo vacio para eliminarlo.";
            return false;
        }

        if (parsed <= 0)
        {
            monthlyIncome = null;
            message = "El ingreso mensual debe ser mayor a cero.";
            return false;
        }

        if (parsed > 999_999_999m)
        {
            monthlyIncome = null;
            message = "El valor supera el limite permitido.";
            return false;
        }

        monthlyIncome = decimal.Round(parsed, 2, MidpointRounding.AwayFromZero);
        message = string.Empty;
        return true;
    }
}
