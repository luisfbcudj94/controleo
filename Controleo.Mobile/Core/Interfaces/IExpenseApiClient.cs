using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Interfaces;

public interface IExpenseApiClient
{
    void ClearAllCaches();
    Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string monthKey, CancellationToken cancellationToken);
    Task<PagedExpenseResult> GetExpensesPageAsync(string monthKey, int pageNumber, int pageSize, string? movementType, string? paymentMethod, string? searchTerm, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAvailableMonthsAsync(CancellationToken cancellationToken);
    Task<SaveExpenseResult> SaveExpenseAsync(ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> UpdateExpenseAsync(string id, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteExpenseAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string monthKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string monthKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpsertBudgetAsync(string movementType, decimal amount, CancellationToken cancellationToken);
    Task<OperationResult> DeleteBudgetAsync(string movementType, CancellationToken cancellationToken);
    Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken);
    Task<int> CountExpensesByMovementTypeAsync(string movementType, CancellationToken cancellationToken);
    Task<OperationResult> DeleteAllByMovementTypeAsync(string movementType, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(CancellationToken cancellationToken);
    Task<PagedRecurringResult> GetRecurringExpensesPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult> SaveRecurringExpenseAsync(string? id, RecurringExpenseUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteRecurringExpenseAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(CancellationToken cancellationToken);
    Task<PagedObligationResult> GetObligationsPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult> SaveObligationAsync(string? id, ObligationUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteObligationAsync(string id, CancellationToken cancellationToken);
    Task<UserProfileResult> GetUserProfileAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpdateMonthlyIncomeAsync(decimal? monthlyIncome, CancellationToken cancellationToken);
    Task<ReportPreviewResult> GetReportPreviewAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    Task<FinancialScoreResult> GetFinancialScoreAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialScoreHistoryItem>> GetFinancialScoreHistoryAsync(int limit, CancellationToken cancellationToken);
    Task<FinancialRecommendationsResult> GetFinancialRecommendationsAsync(DateOnly startDate, DateOnly endDate, bool forceRefresh, CancellationToken cancellationToken);
    Task<ReportFileDownloadResult> DownloadReportCsvAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    Task<ReportFileDownloadResult> DownloadReportPdfAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);

    // --- Goals ---
    Task<IReadOnlyList<SavingsGoalItem>> GetGoalsAsync(CancellationToken cancellationToken);
    Task<SavingsGoalItem?> GetGoalDetailAsync(string goalId, CancellationToken cancellationToken);
    Task<OperationResult> CreateGoalAsync(GoalUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> UpdateGoalAsync(string goalId, GoalUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteGoalAsync(string goalId, CancellationToken cancellationToken);
    Task<OperationResult> AddGoalContributionAsync(string goalId, GoalContributionRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteGoalContributionAsync(string goalId, string contributionId, CancellationToken cancellationToken);
    Task<GoalSimulationResult?> SimulateGoalAsync(string goalId, GoalSimulationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoalAlertItem>> GetGoalAlertsAsync(CancellationToken cancellationToken);

    // --- Coach IA ---
    Task<ChatMessageItem?> SendCoachMessageAsync(string content, CancellationToken cancellationToken);
    Task<ChatHistoryResult> GetCoachHistoryAsync(int limit, CancellationToken cancellationToken);
    Task<OperationResult> ClearCoachHistoryAsync(CancellationToken cancellationToken);
    Task<CoachSuggestionsResult> GetCoachSuggestionsAsync(CancellationToken cancellationToken);
}
