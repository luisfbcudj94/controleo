namespace Controleo.Mobile.Core.Models;

public sealed record ReportPreviewResult(
    bool IsSuccess,
    string Message,
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
    IReadOnlyList<string> Insights)
{
    public static ReportPreviewResult Failure(DateOnly startDate, DateOnly endDate, string message)
    {
        return new ReportPreviewResult(
            false,
            message,
            startDate,
            endDate,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            [],
            [],
            []);
    }
}

public sealed record ReportDailyTrendPoint(
    DateOnly Date,
    decimal Amount,
    int Transactions);
