using Controleo.Domain.Entities;

namespace Controleo.Domain.Interfaces;

public interface IFinancialInsightRepository
{
    Task SaveScoreSnapshotAsync(FinancialScoreSnapshot snapshot, CancellationToken ct);
    Task<IReadOnlyList<FinancialScoreSnapshot>> GetScoreHistoryAsync(string userId, int limit, CancellationToken ct);
    Task<FinancialScoreSnapshot?> GetLatestScoreSnapshotAsync(string userId, CancellationToken ct);
    Task<FinancialRecommendationCache?> GetRecommendationCacheAsync(string userId, CancellationToken ct);
    Task SaveRecommendationCacheAsync(FinancialRecommendationCache cache, CancellationToken ct);
    Task<AiUsageTotals> GetAiUsageTotalsAsync(string userId, DateOnly date, CancellationToken ct);
    Task RecordAiUsageAsync(string userId, DateOnly date, decimal estimatedUsd, CancellationToken ct);
}
