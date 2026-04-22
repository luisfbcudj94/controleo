using Controleo.Application.DTOs;
using Controleo.Domain.Common;

namespace Controleo.Application.Interfaces;

public interface IFinancialInsightsService
{
    Task<UserProfileResponse> GetUserProfileAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateMonthlyIncomeAsync(string userId, decimal? monthlyIncome, CancellationToken ct);
    Task<FinancialScoreResponse> GetFinancialScoreAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<IReadOnlyList<FinancialScoreHistoryItem>> GetScoreHistoryAsync(string userId, int limit, CancellationToken ct);
    Task<FinancialRecommendationResponse> GetRecommendationsAsync(string userId, DateOnly startDate, DateOnly endDate, bool forceRefresh, CancellationToken ct);
}
