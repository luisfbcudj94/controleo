namespace Controleo.Domain.Common;
public sealed record ApiUserContext(
	string UserId,
	string? ExternalId,
	string? Name,
	string? Email,
	bool IsPremium = false,
	bool IsAdmin = false,
	bool IsImpersonating = false,
	string? ActorUserId = null,
	string? ActorName = null,
	string? ActorEmail = null);
