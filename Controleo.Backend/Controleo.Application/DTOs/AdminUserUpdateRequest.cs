namespace Controleo.Application.DTOs;

public sealed record AdminUserUpdateRequest(bool IsPremium, bool IsAdmin, bool IsDisabled);