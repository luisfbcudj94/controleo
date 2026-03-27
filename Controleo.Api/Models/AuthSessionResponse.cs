namespace Controleo.Api.Models;

public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthUserProfile User
);