using Controleo.Domain.Common;
namespace Controleo.Domain.Interfaces;
public interface IAccessTokenValidator
{
    Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? authorizationHeader, CancellationToken ct);
}
