namespace Controleo.Domain.Interfaces;
public sealed record AuthSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, AuthUserProfile User);
public sealed record AuthUserProfile(
    string UserId,
    string Name,
    string Email,
    bool IsPremium = false,
    bool IsAdmin = false,
    bool IsImpersonating = false,
    string? ActorUserId = null,
    string? ActorName = null,
    string? ActorEmail = null);
public interface IUserAuthService
{
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(string name, string email, string password, CancellationToken ct);
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(string email, string password, CancellationToken ct);
    Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> ImpersonateAsync(string actorUserId, string targetUserId, CancellationToken ct);
}
