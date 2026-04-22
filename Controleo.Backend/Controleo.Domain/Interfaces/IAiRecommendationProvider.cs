using Controleo.Domain.Entities;

namespace Controleo.Domain.Interfaces;

public interface IAiRecommendationProvider
{
    Task<AiRecommendationResult> GenerateRecommendationsAsync(
        string compactSummary,
        IReadOnlyList<string> fallbackRecommendations,
        CancellationToken ct);
}
