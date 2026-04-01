using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Features.Auth;

namespace Controleo.Mobile.Features.Settings;

public partial class ProfilePage : ContentPage
{
    private readonly IAuthService _authService;

    public ProfilePage(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var name = _authService.CurrentUserName;
        NameLabel.Text = name;
        EmailLabel.Text = _authService.CurrentUserEmail;
        AvatarLabel.Text = string.IsNullOrWhiteSpace(name)
            ? "C"
            : name.Trim()[0].ToString().ToUpperInvariant();
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
}
