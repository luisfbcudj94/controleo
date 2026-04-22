namespace Controleo.Mobile.Core.Models;

public sealed record UserProfileResult(
    bool IsSuccess,
    string Message,
    string UserId,
    string Name,
    string Email,
    bool IsPremium,
    bool IsAdmin,
    decimal? MonthlyIncome,
    DateTimeOffset UpdatedAt)
{
    public static UserProfileResult Failure(string message)
    {
        return new UserProfileResult(false, message, string.Empty, string.Empty, string.Empty, false, false, null, DateTimeOffset.UtcNow);
    }
}
