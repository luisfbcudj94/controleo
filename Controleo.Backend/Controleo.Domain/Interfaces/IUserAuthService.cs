namespace Controleo.Domain.Interfaces;
public sealed record AuthSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, AuthUserProfile User);
public sealed record AuthUserProfile(string UserId, string Name, string Email);
public interface IUserAuthService
{
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(string name, string email, string password, CancellationToken ct);
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(string email, string password, CancellationToken ct);
}
