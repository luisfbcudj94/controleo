namespace Controleo.Mobile.Interfaces;

public interface IAuthService
{
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string ErrorMessage)> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<(bool IsSuccess, string ErrorMessage)> RegisterAsync(string name, string email, string password, CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    string CurrentUserName { get; }
    string CurrentUserEmail { get; }
    string ApiBaseUrl { get; }
}
