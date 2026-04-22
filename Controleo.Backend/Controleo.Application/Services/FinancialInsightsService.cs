using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Application.Options;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace Controleo.Application.Services;

public sealed class FinancialInsightsService(
    IExpenseRepository expenseRepository,
    IBudgetRepository budgetRepository,
    IObligationRepository obligationRepository,
    IUserProfileRepository userProfileRepository,
    IFinancialInsightRepository financialInsightRepository,
    IAiRecommendationProvider aiRecommendationProvider,
    IOptions<AiBudgetOptions> aiBudgetOptions) : IFinancialInsightsService
{
    private const int MaxRangeDays = 366;
    private const string AlgorithmVersion = "score-v1";

    private readonly AiBudgetOptions _budgetOptions = NormalizeBudgetOptions(aiBudgetOptions.Value);

    public async Task<UserProfileResponse> GetUserProfileAsync(string userId, CancellationToken ct)
    {
        var profile = await userProfileRepository.GetAsync(userId, ct);
        if (profile is null)
        {
            return new UserProfileResponse(userId, string.Empty, string.Empty, false, false, null, DateTimeOffset.UtcNow);
        }

        return new UserProfileResponse(
            profile.UserId,
            profile.Name,
            profile.Email,
            profile.IsPremium,
            profile.IsAdmin,
            profile.MonthlyIncome,
            profile.UpdatedAt);
    }

    public Task<OperationResult> UpdateMonthlyIncomeAsync(string userId, decimal? monthlyIncome, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(new OperationResult(false, "El usuario es requerido."));
        }

        if (monthlyIncome.HasValue)
        {
            if (monthlyIncome.Value <= 0)
            {
                return Task.FromResult(new OperationResult(false, "El ingreso mensual debe ser mayor a cero."));
            }

            if (monthlyIncome.Value > 999_999_999m)
            {
                return Task.FromResult(new OperationResult(false, "El ingreso mensual excede el valor permitido."));
            }

            monthlyIncome = decimal.Round(monthlyIncome.Value, 2, MidpointRounding.AwayFromZero);
        }

        return userProfileRepository.UpdateMonthlyIncomeAsync(userId, monthlyIncome, ct);
    }

    public async Task<FinancialScoreResponse> GetFinancialScoreAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var snapshot = await BuildScoreSnapshotAsync(userId, startDate, endDate, ct);
        await financialInsightRepository.SaveScoreSnapshotAsync(snapshot, ct);
        return MapScore(snapshot);
    }

    public async Task<IReadOnlyList<FinancialScoreHistoryItem>> GetScoreHistoryAsync(string userId, int limit, CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 24);
        var rows = await financialInsightRepository.GetScoreHistoryAsync(userId, safeLimit, ct);
        return rows
            .OrderByDescending(item => item.GeneratedAtUtc)
            .Select(item => new FinancialScoreHistoryItem(
                item.PeriodStart,
                item.PeriodEnd,
                item.Score,
                item.Trend,
                item.Confidence,
                item.GeneratedAtUtc))
            .ToArray();
    }

    public async Task<FinancialRecommendationResponse> GetRecommendationsAsync(string userId, DateOnly startDate, DateOnly endDate, bool forceRefresh, CancellationToken ct)
    {
        var snapshot = await BuildScoreSnapshotAsync(userId, startDate, endDate, ct);
        await financialInsightRepository.SaveScoreSnapshotAsync(snapshot, ct);

        var fallbackRecommendations = BuildHeuristicRecommendations(snapshot);
        var fallbackPriority = ResolvePriority(snapshot.Score);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var cache = await financialInsightRepository.GetRecommendationCacheAsync(userId, ct);
        var scoreDelta = cache is null ? int.MaxValue : Math.Abs(snapshot.Score - cache.ScoreAtGeneration);

        if (cache is not null && !forceRefresh && !IsCacheExpired(cache, now, _budgetOptions.RecommendationCacheDays) && scoreDelta < _budgetOptions.MaterialScoreDelta)
        {
            return new FinancialRecommendationResponse(
                "Cache",
                cache.Priority,
                TrimRecommendations(cache.Recommendations),
                snapshot.Score,
                snapshot.Trend,
                snapshot.Confidence,
                snapshot.Drivers,
                cache.GeneratedAtUtc);
        }

        var usage = await financialInsightRepository.GetAiUsageTotalsAsync(userId, today, ct);
        if (ShouldInvokeAi(forceRefresh, cache, snapshot.Score, scoreDelta)
            && CanSpendBudget(usage, _budgetOptions))
        {
            var aiSummary = BuildCompactAiSummary(snapshot, fallbackRecommendations);
            var aiResult = await aiRecommendationProvider.GenerateRecommendationsAsync(aiSummary, fallbackRecommendations, ct);
            if (aiResult.IsSuccess && aiResult.Recommendations.Count > 0)
            {
                var (recommendations, hasAiContent) = ComposeActionableRecommendations(aiResult.Recommendations, fallbackRecommendations);
                var resolvedSourceLabel = hasAiContent ? aiResult.SourceLabel : "Heuristica local";
                var resolvedPriority = hasAiContent && !string.IsNullOrWhiteSpace(aiResult.Priority)
                    ? aiResult.Priority
                    : fallbackPriority;
                await financialInsightRepository.RecordAiUsageAsync(userId, today, _budgetOptions.EstimatedUsdPerRequest, ct);

                var aiCache = new FinancialRecommendationCache(
                    BuildRecommendationCacheId(userId),
                    userId,
                    resolvedSourceLabel,
                    resolvedPriority,
                    recommendations,
                    snapshot.Score,
                    now);

                await financialInsightRepository.SaveRecommendationCacheAsync(aiCache, ct);

                return new FinancialRecommendationResponse(
                    aiCache.SourceLabel,
                    aiCache.Priority,
                    aiCache.Recommendations,
                    snapshot.Score,
                    snapshot.Trend,
                    snapshot.Confidence,
                    snapshot.Drivers,
                    aiCache.GeneratedAtUtc);
            }
        }

        if (cache is not null && !forceRefresh && !IsCacheExpired(cache, now, _budgetOptions.RecommendationCacheDays))
        {
            return new FinancialRecommendationResponse(
                "Cache",
                cache.Priority,
                TrimRecommendations(cache.Recommendations),
                snapshot.Score,
                snapshot.Trend,
                snapshot.Confidence,
                snapshot.Drivers,
                cache.GeneratedAtUtc);
        }

        var fallbackCache = new FinancialRecommendationCache(
            BuildRecommendationCacheId(userId),
            userId,
            "Heuristica local",
            fallbackPriority,
            fallbackRecommendations,
            snapshot.Score,
            now);

        await financialInsightRepository.SaveRecommendationCacheAsync(fallbackCache, ct);

        return new FinancialRecommendationResponse(
            fallbackCache.SourceLabel,
            fallbackCache.Priority,
            fallbackCache.Recommendations,
            snapshot.Score,
            snapshot.Trend,
            snapshot.Confidence,
            snapshot.Drivers,
            fallbackCache.GeneratedAtUtc);
    }

    private async Task<FinancialScoreSnapshot> BuildScoreSnapshotAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        ValidateDateRange(startDate, endDate);

        var currentExpenses = await LoadExpensesAsync(userId, startDate, endDate, ct);
        var previousRange = ResolvePreviousRange(startDate, endDate);
        var previousExpenses = await LoadExpensesAsync(userId, previousRange.PreviousStartDate, previousRange.PreviousEndDate, ct);

        var budgets = await budgetRepository.GetBudgetsAsync(userId, ct);
        var obligations = await obligationRepository.GetObligationsAsync(userId, ct);
        var profile = await userProfileRepository.GetAsync(userId, ct);

        var totalSpent = currentExpenses.Sum(item => item.Amount);
        var previousTotalSpent = previousExpenses.Sum(item => item.Amount);
        var amountChangePercentage = CalculateChangePercentage(totalSpent, previousTotalSpent);

        var days = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var normalizedMonthlySpend = days <= 0 ? totalSpent : (totalSpent / days) * 30m;

        var monthlyIncome = profile?.MonthlyIncome;
        var activeObligations = obligations.Where(item => item.IsActive).ToArray();
        var monthlyObligationTotal = activeObligations.Sum(item => item.MonthlyPayment);

        var categoryGroups = currentExpenses
            .GroupBy(item => NormalizeLabel(item.MovementType, "Sin categoria"), StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Name = group.Key, Amount = group.Sum(item => item.Amount) })
            .OrderByDescending(item => item.Amount)
            .ToArray();

        var topCategoryShare = totalSpent <= 0 || categoryGroups.Length == 0
            ? 0m
            : Math.Round(categoryGroups[0].Amount / totalSpent, 4, MidpointRounding.AwayFromZero);

        var budgetByCategory = budgets
            .Where(item => item.Amount > 0)
            .GroupBy(item => NormalizeLabel(item.MovementType, string.Empty), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.OrdinalIgnoreCase);

        var scaledBudgetTotal = budgetByCategory.Count == 0
            ? 0m
            : Math.Round(budgetByCategory.Values.Sum() * (days / 30m), 2, MidpointRounding.AwayFromZero);

        var budgetTrackedSpent = currentExpenses
            .Where(item => budgetByCategory.ContainsKey(NormalizeLabel(item.MovementType, string.Empty)))
            .Sum(item => item.Amount);

        var budgetUtilizationRatio = scaledBudgetTotal <= 0
            ? 0m
            : Math.Round(budgetTrackedSpent / scaledBudgetTotal, 4, MidpointRounding.AwayFromZero);

        var spendingToIncomeRatio = monthlyIncome is > 0
            ? (decimal?)Math.Round(normalizedMonthlySpend / monthlyIncome.Value, 4, MidpointRounding.AwayFromZero)
            : null;

        var obligationsToIncomeRatio = monthlyIncome is > 0
            ? (decimal?)Math.Round(monthlyObligationTotal / monthlyIncome.Value, 4, MidpointRounding.AwayFromZero)
            : null;

        var budgetScore = ScoreBudgetHealth(budgetUtilizationRatio, scaledBudgetTotal > 0);
        var trendScore = ScoreTrend(amountChangePercentage);
        var concentrationScore = ScoreConcentration(topCategoryShare);
        var incomeScore = ScoreIncomePressure(spendingToIncomeRatio);
        var obligationScore = ScoreObligationsPressure(obligationsToIncomeRatio);

        var components = new List<FinancialScoreComponent>
        {
            new(
                "budget-discipline",
                "Disciplina de presupuesto",
                budgetScore,
                scaledBudgetTotal > 0 ? 0.24m : 0.08m,
                scaledBudgetTotal > 0
                    ? $"Uso de presupuesto: {budgetUtilizationRatio * 100m:0.##}%"
                    : "No hay presupuestos configurados; score neutral."),
            new(
                "trend",
                "Tendencia de gasto",
                trendScore,
                0.24m,
                $"Cambio vs periodo anterior: {amountChangePercentage:0.##}%"),
            new(
                "concentration",
                "Diversificacion de gasto",
                concentrationScore,
                0.18m,
                $"Categoria principal concentra {topCategoryShare * 100m:0.##}% del gasto."),
            new(
                "income-pressure",
                "Presion sobre ingreso",
                incomeScore,
                monthlyIncome is > 0 ? 0.22m : 0m,
                monthlyIncome is > 0
                    ? $"Gasto mensual proyectado: {spendingToIncomeRatio.GetValueOrDefault() * 100m:0.##}% del ingreso."
                    : "Agrega ingreso mensual para personalizar este componente."),
            new(
                "obligations-pressure",
                "Carga de obligaciones",
                obligationScore,
                monthlyIncome is > 0 ? 0.12m : 0m,
                monthlyIncome is > 0
                    ? $"Obligaciones mensuales: {obligationsToIncomeRatio.GetValueOrDefault() * 100m:0.##}% del ingreso."
                    : "Sin ingreso configurado no se calcula la carga de obligaciones.")
        };

        var weightedScore = CalculateWeightedScore(components);
        var finalScore = Math.Clamp((int)Math.Round(weightedScore, MidpointRounding.AwayFromZero), 0, 100);

        var confidence = CalculateConfidence(
            monthlyIncome,
            budgets.Count,
            activeObligations.Length,
            currentExpenses.Count);

        var previousSnapshot = await financialInsightRepository.GetLatestScoreSnapshotAsync(userId, ct);
        var trend = ResolveTrend(finalScore, previousSnapshot?.Score, amountChangePercentage);

        var drivers = BuildDrivers(
            finalScore,
            amountChangePercentage,
            topCategoryShare,
            spendingToIncomeRatio,
            obligationsToIncomeRatio,
            budgetUtilizationRatio,
            scaledBudgetTotal > 0);

        return new FinancialScoreSnapshot(
            BuildScoreSnapshotId(userId, startDate, endDate),
            userId,
            startDate,
            endDate,
            finalScore,
            trend,
            confidence,
            AlgorithmVersion,
            totalSpent,
            monthlyIncome,
            spendingToIncomeRatio,
            obligationsToIncomeRatio,
            budgetUtilizationRatio,
            amountChangePercentage,
            components,
            drivers,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<ExpenseItem>> LoadExpensesAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var monthCursor = new DateOnly(startDate.Year, startDate.Month, 1);
        var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);
        var result = new List<ExpenseItem>();

        while (monthCursor <= endMonth)
        {
            var monthKey = monthCursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var monthExpenses = await expenseRepository.GetExpensesAsync(userId, monthKey, ct);

            result.AddRange(monthExpenses.Where(item => item.Date >= startDate && item.Date <= endDate));
            monthCursor = monthCursor.AddMonths(1);
        }

        return result
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Amount)
            .ToArray();
    }

    private static (DateOnly PreviousStartDate, DateOnly PreviousEndDate) ResolvePreviousRange(DateOnly startDate, DateOnly endDate)
    {
        var days = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var previousEndDate = startDate.AddDays(-1);
        var previousStartDate = previousEndDate.AddDays(-(days - 1));
        return (previousStartDate, previousEndDate);
    }

    private static FinancialScoreResponse MapScore(FinancialScoreSnapshot snapshot)
    {
        return new FinancialScoreResponse(
            snapshot.PeriodStart,
            snapshot.PeriodEnd,
            snapshot.Score,
            snapshot.Trend,
            snapshot.Confidence,
            snapshot.AlgorithmVersion,
            snapshot.TotalSpent,
            snapshot.MonthlyIncome,
            snapshot.SpendingToIncomeRatio,
            snapshot.ObligationsToIncomeRatio,
            snapshot.BudgetUtilizationRatio,
            snapshot.AmountChangePercentage,
            snapshot.Components
                .Select(item => new FinancialScoreComponentResponse(item.Key, item.Name, item.Score, item.Weight, item.Detail))
                .ToArray(),
            snapshot.Drivers);
    }

    private static decimal CalculateChangePercentage(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return current == 0 ? 0 : 100;
        }

        return Math.Round(((current - previous) / previous) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static int ScoreBudgetHealth(decimal utilizationRatio, bool hasBudget)
    {
        if (!hasBudget)
        {
            return 70;
        }

        return utilizationRatio switch
        {
            <= 0.80m => 95,
            <= 1.00m => 82,
            <= 1.15m => 65,
            <= 1.30m => 45,
            _ => 24
        };
    }

    private static int ScoreTrend(decimal amountChangePercentage)
    {
        return amountChangePercentage switch
        {
            <= -10m => 92,
            <= 0m => 80,
            <= 10m => 68,
            <= 25m => 45,
            _ => 22
        };
    }

    private static int ScoreConcentration(decimal topCategoryShare)
    {
        return topCategoryShare switch
        {
            <= 0.25m => 94,
            <= 0.35m => 80,
            <= 0.50m => 64,
            <= 0.65m => 44,
            _ => 22
        };
    }

    private static int ScoreIncomePressure(decimal? spendingToIncomeRatio)
    {
        if (spendingToIncomeRatio is null)
        {
            return 70;
        }

        return spendingToIncomeRatio.Value switch
        {
            <= 0.50m => 95,
            <= 0.70m => 82,
            <= 0.90m => 66,
            <= 1.00m => 52,
            <= 1.20m => 34,
            _ => 18
        };
    }

    private static int ScoreObligationsPressure(decimal? obligationsToIncomeRatio)
    {
        if (obligationsToIncomeRatio is null)
        {
            return 70;
        }

        return obligationsToIncomeRatio.Value switch
        {
            <= 0.20m => 94,
            <= 0.35m => 75,
            <= 0.50m => 58,
            <= 0.70m => 38,
            _ => 20
        };
    }

    private static decimal CalculateWeightedScore(IReadOnlyList<FinancialScoreComponent> components)
    {
        var active = components.Where(item => item.Weight > 0).ToArray();
        if (active.Length == 0)
        {
            return 0;
        }

        var totalWeight = active.Sum(item => item.Weight);
        if (totalWeight <= 0)
        {
            return 0;
        }

        var weighted = active.Sum(item => item.Score * (item.Weight / totalWeight));
        return Math.Round(weighted, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateConfidence(decimal? monthlyIncome, int budgetsCount, int obligationsCount, int transactionsCount)
    {
        var confidence = 0.52m;

        if (monthlyIncome is > 0)
        {
            confidence += 0.20m;
        }

        if (budgetsCount > 0)
        {
            confidence += 0.14m;
        }

        if (obligationsCount > 0 && monthlyIncome is > 0)
        {
            confidence += 0.05m;
        }

        if (transactionsCount >= 5)
        {
            confidence += 0.06m;
        }

        return Math.Round(Math.Clamp(confidence, 0.45m, 0.95m), 2, MidpointRounding.AwayFromZero);
    }

    private static string ResolveTrend(int currentScore, int? previousScore, decimal amountChangePercentage)
    {
        if (previousScore.HasValue)
        {
            var delta = currentScore - previousScore.Value;
            if (delta >= 3)
            {
                return "up";
            }

            if (delta <= -3)
            {
                return "down";
            }

            return "stable";
        }

        if (amountChangePercentage <= -5)
        {
            return "up";
        }

        if (amountChangePercentage >= 5)
        {
            return "down";
        }

        return "stable";
    }

    private static IReadOnlyList<string> BuildDrivers(
        int score,
        decimal amountChangePercentage,
        decimal topCategoryShare,
        decimal? spendingToIncomeRatio,
        decimal? obligationsToIncomeRatio,
        decimal budgetUtilizationRatio,
        bool hasBudget)
    {
        var drivers = new List<string>();

        if (score >= 80)
        {
            drivers.Add("Tu salud financiera esta en zona fuerte y sostenible.");
        }
        else if (score >= 60)
        {
            drivers.Add("Tu salud financiera es estable, con margen claro de mejora.");
        }
        else
        {
            drivers.Add("Tu salud financiera requiere ajustes prioritarios esta semana.");
        }

        if (amountChangePercentage >= 10)
        {
            drivers.Add($"Tus gastos crecieron {amountChangePercentage:0.##}% frente al periodo anterior.");
        }
        else if (amountChangePercentage <= -8)
        {
            drivers.Add($"Reduciste tus gastos en {Math.Abs(amountChangePercentage):0.##}% frente al periodo anterior.");
        }

        if (topCategoryShare >= 0.45m)
        {
            drivers.Add($"Una categoria concentra {topCategoryShare * 100m:0.##}% de tu gasto.");
        }

        if (spendingToIncomeRatio is > 0.90m)
        {
            drivers.Add($"Tu gasto mensual proyectado equivale a {spendingToIncomeRatio.Value * 100m:0.##}% de tu ingreso.");
        }

        if (obligationsToIncomeRatio is > 0.35m)
        {
            drivers.Add($"Tus obligaciones fijas representan {obligationsToIncomeRatio.Value * 100m:0.##}% de tu ingreso.");
        }

        if (hasBudget && budgetUtilizationRatio > 1.0m)
        {
            drivers.Add($"Estas sobrepasando el presupuesto en {((budgetUtilizationRatio - 1m) * 100m):0.##}%.");
        }

        return drivers
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildHeuristicRecommendations(FinancialScoreSnapshot snapshot)
    {
        var recommendations = new List<string>();

        if (snapshot.SpendingToIncomeRatio is > 0.90m)
        {
            var currentRatioPct = snapshot.SpendingToIncomeRatio.Value * 100m;
            var targetRatioPct = Math.Max(70m, currentRatioPct - 8m);
            recommendations.Add($"Reduce 10% tu gasto variable en 14 dias para bajar gasto/ingreso de {currentRatioPct:0.#}% a ~{targetRatioPct:0.#}%.");
        }

        if (snapshot.BudgetUtilizationRatio > 1.0m)
        {
            var overflowPct = (snapshot.BudgetUtilizationRatio - 1m) * 100m;
            recommendations.Add($"Recorta {Math.Max(5m, overflowPct):0.#}% en categorias excedidas durante 14 dias para volver a <=100% del presupuesto.");
        }

        if (snapshot.AmountChangePercentage > 10)
        {
            var reductionTarget = Math.Min(15m, Math.Max(6m, snapshot.AmountChangePercentage / 2m));
            recommendations.Add($"Recorta al menos {reductionTarget:0.#}% en 7 dias para revertir el alza de {snapshot.AmountChangePercentage:0.#}% vs periodo anterior.");
        }

        if (snapshot.ObligationsToIncomeRatio is > 0.35m)
        {
            var obligationsPct = snapshot.ObligationsToIncomeRatio.Value * 100m;
            recommendations.Add($"Renegocia 1 obligacion en 30 dias para bajar obligaciones/ingreso de {obligationsPct:0.#}% a <35%.");
        }

        if (snapshot.Score >= 80)
        {
            if (snapshot.MonthlyIncome is > 0)
            {
                var monthlySaving = snapshot.MonthlyIncome.Value * 0.10m;
                recommendations.Add($"Automatiza ahorro de {monthlySaving:0.##} por 30 dias para sostener score >=80 y crear colchon.");
            }
            else
            {
                recommendations.Add("Automatiza ahorro de 10% por 30 dias para sostener score >=80 y crear colchon.");
            }
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Revisa gastos cada 3 dias y recorta 5% en tu categoria principal durante 14 dias.");
            recommendations.Add("Reserva 10% de tu ingreso por 30 dias para construir fondo de respaldo.");
        }

        return TrimRecommendations(recommendations);
    }

    private static IReadOnlyList<string> TrimRecommendations(IEnumerable<string> recommendations)
    {
        return recommendations
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
    }

    private static (IReadOnlyList<string> Recommendations, bool HasAiContent) ComposeActionableRecommendations(
        IReadOnlyList<string> aiRecommendations,
        IReadOnlyList<string> fallbackRecommendations)
    {
        var actionableAi = TrimRecommendations(aiRecommendations.Where(IsActionableRecommendation));
        if (actionableAi.Count == 0)
        {
            return (TrimRecommendations(fallbackRecommendations), false);
        }

        var merged = TrimRecommendations(actionableAi.Concat(fallbackRecommendations));
        return (merged, true);
    }

    private static bool IsActionableRecommendation(string recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return false;
        }

        var text = recommendation.Trim();
        return text.Any(char.IsDigit) && ContainsTimeHorizon(text);
    }

    private static bool ContainsTimeHorizon(string text)
    {
        var normalized = text.ToLowerInvariant();
        return normalized.Contains("dia")
            || normalized.Contains("dias")
            || normalized.Contains("semana")
            || normalized.Contains("semanas")
            || normalized.Contains("mes")
            || normalized.Contains("meses");
    }

    private static string ResolvePriority(int score)
    {
        return score switch
        {
            < 55 => "Alta",
            < 75 => "Media",
            _ => "Baja"
        };
    }

    private static bool IsCacheExpired(FinancialRecommendationCache cache, DateTimeOffset nowUtc, int maxDays)
    {
        return nowUtc - cache.GeneratedAtUtc > TimeSpan.FromDays(Math.Max(maxDays, 1));
    }

    private bool ShouldInvokeAi(bool forceRefresh, FinancialRecommendationCache? cache, int currentScore, int scoreDelta)
    {
        if (forceRefresh)
        {
            return true;
        }

        if (cache is null)
        {
            return true;
        }

        if (scoreDelta >= _budgetOptions.MaterialScoreDelta)
        {
            return true;
        }

        if (currentScore < 55)
        {
            return true;
        }

        return false;
    }

    private static bool CanSpendBudget(AiUsageTotals usage, AiBudgetOptions options)
    {
        if (usage.GlobalMonthlyEstimatedUsd + options.EstimatedUsdPerRequest > options.MonthlyUsdCap)
        {
            return false;
        }

        if (usage.DailyRequestCount + 1 > options.DailyRequestCap)
        {
            return false;
        }

        if (usage.UserMonthlyRequestCount + 1 > options.PerUserMonthlyRequestCap)
        {
            return false;
        }

        return true;
    }

    private static string BuildCompactAiSummary(FinancialScoreSnapshot snapshot, IReadOnlyList<string> fallbackRecommendations)
    {
        var days = Math.Max(1, snapshot.PeriodEnd.DayNumber - snapshot.PeriodStart.DayNumber + 1);
        var spendingRatio = snapshot.SpendingToIncomeRatio is null
            ? "na"
            : snapshot.SpendingToIncomeRatio.Value.ToString("0.####", CultureInfo.InvariantCulture);

        var obligationsRatio = snapshot.ObligationsToIncomeRatio is null
            ? "na"
            : snapshot.ObligationsToIncomeRatio.Value.ToString("0.####", CultureInfo.InvariantCulture);

        var budgetRatio = snapshot.BudgetUtilizationRatio <= 0
            ? "na"
            : snapshot.BudgetUtilizationRatio.ToString("0.####", CultureInfo.InvariantCulture);

        var monthlyIncome = snapshot.MonthlyIncome is > 0
            ? snapshot.MonthlyIncome.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "na";

        var weakestSignals = string.Join(",",
            snapshot.Components
                .OrderBy(item => item.Score)
                .Take(2)
                .Select(item => $"{ToSignalCode(item.Key)}:{item.Score}"));

        var componentVector = string.Join(",",
            snapshot.Components
                .Select(item => $"{ToSignalCode(item.Key)}:{item.Score}"));

        var fallback = string.Join(" | ", fallbackRecommendations);
        var drivers = string.Join(" | ", snapshot.Drivers);

        return string.Join('\n',
            "Contexto financiero agregado (sin datos personales ni transacciones crudas):",
            $"period_days={days}",
            $"score={snapshot.Score};risk={ResolveRiskBand(snapshot.Score)};trend={snapshot.Trend};confidence={snapshot.Confidence:0.##}",
            $"total_spent={snapshot.TotalSpent:0.##};monthly_income={monthlyIncome}",
            $"spending_to_income={spendingRatio}",
            $"obligations_to_income={obligationsRatio}",
            $"budget_utilization={budgetRatio}",
            $"amount_change_pct={snapshot.AmountChangePercentage:0.##}",
            $"signal_vector={componentVector}",
            $"weakest_signals={weakestSignals}",
            $"drivers={drivers}",
            $"fallback={fallback}",
            "Devuelve JSON estricto con este esquema: {\"priority\":\"Alta|Media|Baja\",\"recommendations\":[\"...\",\"...\",\"...\"]}.",
            "Reglas: maximo 3 recomendaciones, cada una con accion + meta numerica + horizonte (dias o semanas), tono breve en espanol, usando los datos del contexto.");
    }

    private static string ResolveRiskBand(int score)
    {
        return score switch
        {
            < 55 => "high",
            < 75 => "medium",
            _ => "low"
        };
    }

    private static string ToSignalCode(string key)
    {
        return key switch
        {
            "budget-discipline" => "bdg",
            "trend" => "trd",
            "concentration" => "cnc",
            "income-pressure" => "inc",
            "obligations-pressure" => "obl",
            _ => "oth"
        };
    }

    private static AiBudgetOptions NormalizeBudgetOptions(AiBudgetOptions options)
    {
        return new AiBudgetOptions
        {
            MonthlyUsdCap = options.MonthlyUsdCap <= 0 ? 2m : options.MonthlyUsdCap,
            DailyRequestCap = options.DailyRequestCap <= 0 ? 50 : options.DailyRequestCap,
            PerUserMonthlyRequestCap = options.PerUserMonthlyRequestCap <= 0 ? 20 : options.PerUserMonthlyRequestCap,
            EstimatedUsdPerRequest = options.EstimatedUsdPerRequest <= 0 ? 0.003m : options.EstimatedUsdPerRequest,
            RecommendationCacheDays = options.RecommendationCacheDays <= 0 ? 10 : options.RecommendationCacheDays,
            MaterialScoreDelta = options.MaterialScoreDelta <= 0 ? 7 : options.MaterialScoreDelta
        };
    }

    private static void ValidateDateRange(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate)
        {
            throw new ArgumentException("La fecha inicial no puede ser mayor que la fecha final.");
        }

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        if (days > MaxRangeDays)
        {
            throw new ArgumentException("El rango maximo permitido es de 12 meses.");
        }
    }

    private static string BuildScoreSnapshotId(string userId, DateOnly startDate, DateOnly endDate)
    {
        var normalizedUserId = userId.Trim().Replace("/", "_").ToLowerInvariant();
        return $"financial-score-{normalizedUserId}-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}";
    }

    private static string BuildRecommendationCacheId(string userId)
    {
        var normalizedUserId = userId.Trim().Replace("/", "_").ToLowerInvariant();
        return $"financial-recommendations-{normalizedUserId}";
    }

    private static string NormalizeLabel(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
