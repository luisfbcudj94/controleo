namespace Controleo.Application.DTOs;

public sealed record UserProfileResponse(
    string UserId,
    string Name,
    string Email,
    bool IsPremium,
    bool IsAdmin,
    decimal? MonthlyIncome,
    DateTimeOffset UpdatedAt);

public sealed record UpdateMonthlyIncomeRequest(decimal? MonthlyIncome);
