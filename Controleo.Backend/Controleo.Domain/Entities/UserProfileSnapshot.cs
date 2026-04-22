namespace Controleo.Domain.Entities;

public sealed record UserProfileSnapshot(
    string UserId,
    string Name,
    string Email,
    bool IsPremium,
    bool IsAdmin,
    decimal? MonthlyIncome,
    DateTimeOffset UpdatedAt);
