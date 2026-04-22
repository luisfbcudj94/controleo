namespace Controleo.Application.Options;

public sealed class AiBudgetOptions
{
    public const string SectionName = "AiBudget";

    public decimal MonthlyUsdCap { get; set; } = 2m;
    public int DailyRequestCap { get; set; } = 50;
    public int PerUserMonthlyRequestCap { get; set; } = 20;
    public decimal EstimatedUsdPerRequest { get; set; } = 0.003m;
    public int RecommendationCacheDays { get; set; } = 10;
    public int MaterialScoreDelta { get; set; } = 7;
}
