namespace Controleo.Domain.Entities;

public sealed record AdminUserItem(
    string UserId,
    string Name,
    string Email,
    bool IsPremium,
    bool IsAdmin,
    bool IsSuperAdmin,
    bool IsDisabled,
    string CreatedAt,
    string UpdatedAt,
    string LastLoginAt);