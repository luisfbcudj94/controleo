namespace Controleo.Api.Models;

public sealed record AuthRegisterRequest(
    string Name,
    string Email,
    string Password
);