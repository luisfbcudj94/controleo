using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Services;

namespace Controleo.Mobile.Features.Auth;

public partial class LoginPage : ContentPage
{
    private readonly IAuthService _authService;
    private bool _registerMode;
    private bool _welcomeShown;

    public LoginPage(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await ShowWelcomeAsync();
        SetBusy(true, "Validando sesión...");
        var isAuthenticated = await _authService.IsAuthenticatedAsync();
        if (isAuthenticated)
        {
            var app = (App)Application.Current!;
            await app.WarmUpCatalogVisualsAsync();
            app.ShowMainApp();
            return;
        }

        SetBusy(false, $"API: {_authService.ApiBaseUrl}");
    }

    private async Task ShowWelcomeAsync()
    {
        if (_welcomeShown || !WelcomeOverlay.IsVisible)
        {
            return;
        }

        _welcomeShown = true;
        try
        {
            await WelcomeContent.ScaleTo(1, 250, Easing.CubicOut);
            await Task.Delay(320);

            await Task.WhenAll(
                WelcomeContent.FadeTo(0, 220, Easing.CubicIn),
                WelcomeContent.ScaleTo(0.96, 220, Easing.CubicIn));
        }
        catch
        {
        }
        finally
        {
            WelcomeOverlay.IsVisible = false;
            WelcomeContent.Opacity = 1;
            WelcomeContent.Scale = 1;
        }
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        if (_registerMode)
        {
            ToggleMode(false);
        }

        SetBusy(true, "Conectando...");

        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text?.Trim() ?? string.Empty;

        var result = await _authService.SignInAsync(email, password);
        if (!result.IsSuccess)
        {
            SetBusy(false, $"No fue posible iniciar sesión: {result.ErrorMessage}");
            return;
        }

        var app = (App)Application.Current!;
        await app.WarmUpCatalogVisualsAsync();
        app.ShowMainApp();
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        if (!_registerMode)
        {
            ToggleMode(true);
        }

        SetBusy(true, "Creando cuenta...");

        var name = NameEntry.Text?.Trim() ?? string.Empty;
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text?.Trim() ?? string.Empty;
        var confirmPassword = ConfirmPasswordEntry.Text?.Trim() ?? string.Empty;

        if (password != confirmPassword)
        {
            SetBusy(false, "Las contraseñas no coinciden.");
            return;
        }

        var result = await _authService.RegisterAsync(name, email, password);
        if (!result.IsSuccess)
        {
            SetBusy(false, $"No fue posible registrar: {result.ErrorMessage}");
            return;
        }

        var app = (App)Application.Current!;
        await app.WarmUpCatalogVisualsAsync();
        app.ShowMainApp();
    }

    private void OnToggleModeClicked(object? sender, EventArgs e)
    {
        ToggleMode(!_registerMode);
        StatusLabel.IsVisible = false;
    }

    private void ToggleMode(bool enableRegisterMode)
    {
        _registerMode = enableRegisterMode;
        NameEntry.IsVisible = enableRegisterMode;
        ConfirmPasswordEntry.IsVisible = enableRegisterMode;
        RegisterButton.IsVisible = enableRegisterMode;
        SignInButton.IsVisible = !enableRegisterMode;
        ModeButton.Text = enableRegisterMode
            ? "Ya tienes cuenta, inicia sesión"
            : "Si no tienes cuenta, regístrate";
    }

    private void SetBusy(bool isBusy, string message)
    {
        LoadingOverlay.IsVisible = isBusy;
        SignInButton.IsEnabled = !isBusy;
        RegisterButton.IsEnabled = !isBusy;
        ModeButton.IsEnabled = !isBusy;
        NameEntry.IsEnabled = !isBusy;
        EmailEntry.IsEnabled = !isBusy;
        PasswordEntry.IsEnabled = !isBusy;
        ConfirmPasswordEntry.IsEnabled = !isBusy;

        StatusLabel.IsVisible = true;
        StatusLabel.Text = message;

        if (isBusy)
        {
            return;
        }
    }
}
