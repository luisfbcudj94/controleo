using Controleo.Mobile.Interfaces;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly IAuthService _authService;
    private bool _registerMode;

    public LoginPage(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        StatusLabel.IsVisible = true;
        StatusLabel.Text = $"API: {_authService.ApiBaseUrl}";

        var isAuthenticated = await _authService.IsAuthenticatedAsync();
        if (isAuthenticated)
        {
            ((App)Application.Current!).ShowMainApp();
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

        ((App)Application.Current!).ShowMainApp();
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

        ((App)Application.Current!).ShowMainApp();
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
