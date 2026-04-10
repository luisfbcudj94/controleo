namespace Controleo.Application.DTOs;

public sealed record ReportPreviewResponse(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalTransactions,
    decimal TotalAmount,
    decimal AverageDailyAmount,
    int PreviousPeriodTransactions,
    decimal PreviousPeriodAmount,
    decimal AmountChangePercentage,
    decimal TransactionsChangePercentage,
    IReadOnlyList<ReportBreakdownItem> CategoryBreakdown,
    IReadOnlyList<ReportBreakdownItem> PaymentMethodBreakdown,
    IReadOnlyList<ReportTopExpenseItem> TopExpenses,
    IReadOnlyList<ReportDailyTrendPoint> DailyTrend,
    IReadOnlyList<string> Insights);

public sealed record ReportBreakdownItem(
    string Name,
    decimal Amount,
    int Count,
    decimal Percentage);

public sealed record ReportTopExpenseItem(
    DateOnly Date,
    string Description,
    string MovementType,
    string PaymentMethod,
    decimal Amount);

public sealed record ReportDailyTrendPoint(
    DateOnly Date,
    decimal Amount,
    int Transactions);
