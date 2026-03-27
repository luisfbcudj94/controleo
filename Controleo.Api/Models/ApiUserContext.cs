namespace Controleo.Api.Models;

public sealed record ApiUserContext(
    string UserId,
    string? ExternalId,
    string? Name,
    string? Email
);
