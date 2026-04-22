using System.Globalization;
using System.Net;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosFinancialInsightRepository(CosmosContainerProvider provider) : IFinancialInsightRepository
{
    public async Task SaveScoreSnapshotAsync(FinancialScoreSnapshot snapshot, CancellationToken ct)
    {
        var doc = new FinancialScoreSnapshotDocument
        {
            Id = snapshot.SnapshotId,
            Type = "financial-score-snapshot",
            UserId = snapshot.UserId,
            PeriodStart = snapshot.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEnd = snapshot.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Score = snapshot.Score,
            Trend = snapshot.Trend,
            Confidence = snapshot.Confidence,
            AlgorithmVersion = snapshot.AlgorithmVersion,
            TotalSpent = snapshot.TotalSpent,
            MonthlyIncome = snapshot.MonthlyIncome,
            SpendingToIncomeRatio = snapshot.SpendingToIncomeRatio,
            ObligationsToIncomeRatio = snapshot.ObligationsToIncomeRatio,
            BudgetUtilizationRatio = snapshot.BudgetUtilizationRatio,
            AmountChangePercentage = snapshot.AmountChangePercentage,
            Components = snapshot.Components
                .Select(item => new FinancialScoreComponentDocument
                {
                    Key = item.Key,
                    Name = item.Name,
                    Score = item.Score,
                    Weight = item.Weight,
                    Detail = item.Detail
                })
                .ToArray(),
            Drivers = snapshot.Drivers.ToArray(),
            GeneratedAt = snapshot.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await provider.Settings.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public async Task<IReadOnlyList<FinancialScoreSnapshot>> GetScoreHistoryAsync(string userId, int limit, CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 24);

        var rows = await CosmosHelper.QueryAsync<FinancialScoreSnapshotDocument>(
            provider.Settings,
            new QueryDefinition("SELECT * FROM c WHERE c.type = @type AND c.userId = @userId ORDER BY c.generatedAt DESC")
                .WithParameter("@type", "financial-score-snapshot")
                .WithParameter("@userId", userId),
            ct);

        return rows
            .Take(safeLimit)
            .Select(MapScoreSnapshot)
            .Where(item => item is not null)
            .Cast<FinancialScoreSnapshot>()
            .ToArray();
    }

    public async Task<FinancialScoreSnapshot?> GetLatestScoreSnapshotAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<FinancialScoreSnapshotDocument>(
            provider.Settings,
            new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.type = @type AND c.userId = @userId ORDER BY c.generatedAt DESC")
                .WithParameter("@type", "financial-score-snapshot")
                .WithParameter("@userId", userId),
            ct);

        return MapScoreSnapshot(rows.FirstOrDefault());
    }

    public async Task<FinancialRecommendationCache?> GetRecommendationCacheAsync(string userId, CancellationToken ct)
    {
        var cacheId = BuildRecommendationCacheId(userId);
        try
        {
            var response = await provider.Settings.ReadItemAsync<FinancialRecommendationCacheDocument>(cacheId, new PartitionKey(cacheId), cancellationToken: ct);
            return MapRecommendation(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveRecommendationCacheAsync(FinancialRecommendationCache cache, CancellationToken ct)
    {
        var doc = new FinancialRecommendationCacheDocument
        {
            Id = cache.CacheId,
            Type = "financial-recommendation-cache",
            UserId = cache.UserId,
            SourceLabel = cache.SourceLabel,
            Priority = cache.Priority,
            Recommendations = cache.Recommendations.ToArray(),
            ScoreAtGeneration = cache.ScoreAtGeneration,
            GeneratedAt = cache.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await provider.Settings.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public async Task<AiUsageTotals> GetAiUsageTotalsAsync(string userId, DateOnly date, CancellationToken ct)
    {
        var dailyDoc = await ReadUsageDocAsync(BuildDailyUsageId(date), ct);
        var monthlyGlobalDoc = await ReadUsageDocAsync(BuildGlobalMonthlyUsageId(date), ct);
        var monthlyUserDoc = await ReadUsageDocAsync(BuildUserMonthlyUsageId(userId, date), ct);

        return new AiUsageTotals(
            dailyDoc?.RequestCount ?? 0,
            monthlyUserDoc?.RequestCount ?? 0,
            monthlyGlobalDoc?.RequestCount ?? 0,
            monthlyUserDoc?.EstimatedUsd ?? 0,
            monthlyGlobalDoc?.EstimatedUsd ?? 0);
    }

    public async Task RecordAiUsageAsync(string userId, DateOnly date, decimal estimatedUsd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await IncrementUsageAsync(
            BuildDailyUsageId(date),
            "ai-usage",
            "daily",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            null,
            estimatedUsd,
            now,
            ct);

        await IncrementUsageAsync(
            BuildGlobalMonthlyUsageId(date),
            "ai-usage",
            "global-monthly",
            date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            null,
            estimatedUsd,
            now,
            ct);

        await IncrementUsageAsync(
            BuildUserMonthlyUsageId(userId, date),
            "ai-usage",
            "user-monthly",
            date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            userId,
            estimatedUsd,
            now,
            ct);
    }

    private async Task IncrementUsageAsync(
        string id,
        string type,
        string scope,
        string periodKey,
        string? userId,
        decimal estimatedUsd,
        string now,
        CancellationToken ct)
    {
        var existing = await ReadUsageDocAsync(id, ct);
        var next = existing ?? new AiUsageDocument
        {
            Id = id,
            Type = type,
            Scope = scope,
            PeriodKey = periodKey,
            UserId = userId,
            RequestCount = 0,
            EstimatedUsd = 0,
            UpdatedAt = now
        };

        next.RequestCount += 1;
        next.EstimatedUsd = Math.Round(next.EstimatedUsd + estimatedUsd, 6, MidpointRounding.AwayFromZero);
        next.UpdatedAt = now;

        await provider.Settings.UpsertItemAsync(next, new PartitionKey(next.Id), cancellationToken: ct);
    }

    private async Task<AiUsageDocument?> ReadUsageDocAsync(string id, CancellationToken ct)
    {
        try
        {
            var response = await provider.Settings.ReadItemAsync<AiUsageDocument>(id, new PartitionKey(id), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static FinancialScoreSnapshot? MapScoreSnapshot(FinancialScoreSnapshotDocument? doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.UserId))
        {
            return null;
        }

        if (!DateOnly.TryParse(doc.PeriodStart, out var periodStart))
        {
            return null;
        }

        if (!DateOnly.TryParse(doc.PeriodEnd, out var periodEnd))
        {
            return null;
        }

        var components = (doc.Components ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .Select(item => new FinancialScoreComponent(
                item.Key ?? string.Empty,
                item.Name ?? string.Empty,
                item.Score,
                item.Weight,
                item.Detail ?? string.Empty))
            .ToArray();

        return new FinancialScoreSnapshot(
            doc.Id,
            doc.UserId,
            periodStart,
            periodEnd,
            doc.Score,
            doc.Trend ?? "stable",
            doc.Confidence,
            string.IsNullOrWhiteSpace(doc.AlgorithmVersion) ? "score-v1" : doc.AlgorithmVersion,
            doc.TotalSpent,
            doc.MonthlyIncome,
            doc.SpendingToIncomeRatio,
            doc.ObligationsToIncomeRatio,
            doc.BudgetUtilizationRatio,
            doc.AmountChangePercentage,
            components,
            (doc.Drivers ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            CosmosHelper.ParseDateTimeOffset(doc.GeneratedAt, DateTimeOffset.UtcNow));
    }

    private static FinancialRecommendationCache? MapRecommendation(FinancialRecommendationCacheDocument? doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.UserId))
        {
            return null;
        }

        return new FinancialRecommendationCache(
            doc.Id,
            doc.UserId,
            string.IsNullOrWhiteSpace(doc.SourceLabel) ? "Heuristica local" : doc.SourceLabel,
            string.IsNullOrWhiteSpace(doc.Priority) ? "Media" : doc.Priority,
            (doc.Recommendations ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            doc.ScoreAtGeneration,
            CosmosHelper.ParseDateTimeOffset(doc.GeneratedAt, DateTimeOffset.UtcNow));
    }

    private static string BuildRecommendationCacheId(string userId)
    {
        var normalized = userId.Trim().Replace("/", "_").ToLowerInvariant();
        return $"financial-recommendations-{normalized}";
    }

    private static string BuildDailyUsageId(DateOnly date)
        => $"ai-usage-daily-{date:yyyyMMdd}";

    private static string BuildGlobalMonthlyUsageId(DateOnly date)
        => $"ai-usage-global-{date:yyyyMM}";

    private static string BuildUserMonthlyUsageId(string userId, DateOnly date)
    {
        var normalized = userId.Trim().Replace("/", "_").ToLowerInvariant();
        return $"ai-usage-user-{normalized}-{date:yyyyMM}";
    }

    private sealed class FinancialScoreSnapshotDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string PeriodStart { get; set; } = string.Empty;
        public string PeriodEnd { get; set; } = string.Empty;
        public int Score { get; set; }
        public string? Trend { get; set; }
        public decimal Confidence { get; set; }
        public string? AlgorithmVersion { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal? MonthlyIncome { get; set; }
        public decimal? SpendingToIncomeRatio { get; set; }
        public decimal? ObligationsToIncomeRatio { get; set; }
        public decimal BudgetUtilizationRatio { get; set; }
        public decimal AmountChangePercentage { get; set; }
        public FinancialScoreComponentDocument[]? Components { get; set; }
        public string[]? Drivers { get; set; }
        public string? GeneratedAt { get; set; }
        public string? UpdatedAt { get; set; }
    }

    private sealed class FinancialScoreComponentDocument
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public int Score { get; set; }
        public decimal Weight { get; set; }
        public string? Detail { get; set; }
    }

    private sealed class FinancialRecommendationCacheDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? SourceLabel { get; set; }
        public string? Priority { get; set; }
        public string[]? Recommendations { get; set; }
        public int ScoreAtGeneration { get; set; }
        public string? GeneratedAt { get; set; }
        public string? UpdatedAt { get; set; }
    }

    private sealed class AiUsageDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string PeriodKey { get; set; } = string.Empty;
        public int RequestCount { get; set; }
        public decimal EstimatedUsd { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
