using Controleo.Api.Models;

namespace Controleo.Api.Services.Auth;

public interface IAccessTokenValidator
{
    Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken);
}
