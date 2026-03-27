using Controleo.Api.Models;

namespace Controleo.Api.Services.Auth;

public interface IUserAuthService
{
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(AuthRegisterRequest request, CancellationToken cancellationToken);
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(AuthLoginRequest request, CancellationToken cancellationToken);
}