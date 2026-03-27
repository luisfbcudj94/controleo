namespace Controleo.Api.Models;

public sealed record AuthUserProfile(
    string UserId,
    string Name,
    string Email
);