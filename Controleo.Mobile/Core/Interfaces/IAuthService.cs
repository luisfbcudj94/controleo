namespace Controleo.Mobile.Core.Interfaces;

public interface IAuthService
{
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string ErrorMessage)> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string ErrorMessage)> RegisterAsync(string name, string email, string password, CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    event Action? SessionCleared;
    string CurrentUserId { get; }
    string CurrentUserName { get; }
    string CurrentUserEmail { get; }
    bool IsCurrentUserPremium { get; }
    string ApiBaseUrl { get; }
}
