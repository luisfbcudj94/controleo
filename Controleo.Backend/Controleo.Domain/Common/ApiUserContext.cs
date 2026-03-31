namespace Controleo.Domain.Common;
public sealed record ApiUserContext(string UserId, string? ExternalId, string? Name, string? Email);
