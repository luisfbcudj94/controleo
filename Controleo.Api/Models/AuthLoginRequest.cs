namespace Controleo.Api.Models;

public sealed record AuthLoginRequest(
    string Email,
    string Password
);