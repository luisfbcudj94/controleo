using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class SavingsGoalService(
    ISavingsGoalRepository goalRepo,
    IExpenseRepository expenseRepo,
    IUserProfileRepository profileRepo) : ISavingsGoalService
{
    public async Task<IReadOnlyList<GoalProgressResponse>> GetAllGoalsAsync(string userId, CancellationToken ct)
    {
        var goals = await goalRepo.GetGoalsAsync(userId, ct);
        var profile = await profileRepo.GetAsync(userId, ct);
        var monthlyIncome = profile?.MonthlyIncome;
        var avgExpenses = await ComputeAverageMonthlyExpensesAsync(userId, ct);
        var available = monthlyIncome.HasValue ? monthlyIncome.Value - avgExpenses : (decimal?)null;

        var results = new List<GoalProgressResponse>(goals.Count);
        foreach (var g in goals)
        {
            var contributions = await goalRepo.GetContributionsAsync(userId, g.Id, ct);
            results.Add(BuildProgress(g, contributions, available));
        }
        return results;
    }

    public async Task<GoalProgressResponse?> GetGoalProgressAsync(string userId, string goalId, CancellationToken ct)
    {
        var goal = await goalRepo.GetGoalByIdAsync(userId, goalId, ct);
        if (goal is null) return null;

        var profile = await profileRepo.GetAsync(userId, ct);
        var monthlyIncome = profile?.MonthlyIncome;
        var avgExpenses = await ComputeAverageMonthlyExpensesAsync(userId, ct);
        var available = monthlyIncome.HasValue ? monthlyIncome.Value - avgExpenses : (decimal?)null;
        var contributions = await goalRepo.GetContributionsAsync(userId, goalId, ct);
        return BuildProgress(goal, contributions, available);
    }

    public Task<OperationResult> CreateGoalAsync(string userId, GoalUpsertRequest request, CancellationToken ct)
    {
        return goalRepo.CreateGoalAsync(userId, request.Name, request.Icon, request.TargetAmount, request.TargetDate, string.IsNullOrWhiteSpace(request.Priority) ? "Media" : request.Priority.Trim(), ct);
    }

    public Task<OperationResult> UpdateGoalAsync(string userId, string goalId, GoalUpsertRequest request, CancellationToken ct)
    {
        return goalRepo.UpdateGoalAsync(userId, goalId, request.Name, request.Icon, request.TargetAmount, request.TargetDate, string.IsNullOrWhiteSpace(request.Priority) ? "Media" : request.Priority.Trim(), string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim(), ct);
    }

    public Task<OperationResult> DeleteGoalAsync(string userId, string goalId, CancellationToken ct)
    {
        return goalRepo.DeleteGoalAsync(userId, goalId, ct);
    }

    public Task<OperationResult> AddContributionAsync(string userId, string goalId, GoalContributionRequest request, CancellationToken ct)
    {
        return goalRepo.AddContributionAsync(userId, goalId, request.Amount, request.Date, request.Note, ct);
    }

    public Task<OperationResult> DeleteContributionAsync(string userId, string goalId, string contributionId, CancellationToken ct)
    {
        return goalRepo.DeleteContributionAsync(userId, goalId, contributionId, ct);
    }

    public async Task<GoalSimulationResponse> SimulateScenarioAsync(string userId, string goalId, GoalSimulationRequest request, CancellationToken ct)
    {
        var goal = await goalRepo.GetGoalByIdAsync(userId, goalId, ct);
        if (goal is null) return new GoalSimulationResponse(null, 0, "Error", "Meta no encontrada.");

        var remaining = Math.Max(0, goal.TargetAmount - goal.CurrentAmount);
        if (remaining <= 0) return new GoalSimulationResponse(goal.TargetDate, 0, "Completada", "¡Ya alcanzaste tu meta!");

        var scenario = request.ScenarioType.Trim();
        if (string.Equals(scenario, "ChangeAmount", StringComparison.OrdinalIgnoreCase))
        {
            var monthlyAmount = request.NewValue;
            if (monthlyAmount <= 0) return new GoalSimulationResponse(null, monthlyAmount, "Inviable", "El aporte mensual debe ser mayor a cero.");
            var monthsNeeded = (int)Math.Ceiling(remaining / monthlyAmount);
            var projectedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(monthsNeeded);
            var feasibility = projectedDate <= goal.TargetDate ? "Viable" : "Requiere más tiempo";
            var desc = projectedDate <= goal.TargetDate
                ? $"Con ${monthlyAmount:N0}/mes, completarías tu meta en {monthsNeeded} meses ({projectedDate:yyyy-MM-dd})."
                : $"Con ${monthlyAmount:N0}/mes, necesitarías {monthsNeeded} meses. Tu fecha objetivo es {goal.TargetDate:yyyy-MM-dd}.";
            return new GoalSimulationResponse(projectedDate, monthlyAmount, feasibility, desc);
        }

        if (string.Equals(scenario, "ChangeDate", StringComparison.OrdinalIgnoreCase))
        {
            var newTargetOrdinal = (int)request.NewValue;
            var newTargetDate = DateOnly.FromDayNumber(newTargetOrdinal);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthsDiff = ((newTargetDate.Year - today.Year) * 12) + newTargetDate.Month - today.Month;
            if (monthsDiff <= 0) return new GoalSimulationResponse(newTargetDate, remaining, "Inviable", "La nueva fecha ya pasó o es este mes.");
            var requiredMonthly = Math.Ceiling(remaining / monthsDiff * 100) / 100;
            return new GoalSimulationResponse(newTargetDate, requiredMonthly, "Viable", $"Para completar el {newTargetDate:yyyy-MM-dd}, necesitarías aportar ~${requiredMonthly:N0}/mes.");
        }

        return new GoalSimulationResponse(null, 0, "Error", "Tipo de escenario no reconocido.");
    }

    public async Task<IReadOnlyList<GoalAlertResponse>> GetGoalAlertsAsync(string userId, CancellationToken ct)
    {
        var goals = await goalRepo.GetGoalsAsync(userId, ct);
        var alerts = new List<GoalAlertResponse>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var g in goals.Where(g => g.Status == "Active"))
        {
            var remaining = Math.Max(0, g.TargetAmount - g.CurrentAmount);
            var daysLeft = g.TargetDate.DayNumber - today.DayNumber;
            var monthsLeft = Math.Max(1, ((g.TargetDate.Year - today.Year) * 12) + g.TargetDate.Month - today.Month);
            var suggestedMonthly = remaining / monthsLeft;

            if (daysLeft < 0)
            {
                alerts.Add(new GoalAlertResponse(g.Id, g.Name, $"La meta '{g.Name}' venció el {g.TargetDate:dd/MM/yyyy}. Considera ajustar la fecha.", "high"));
            }
            else if (daysLeft <= 30 && remaining > 0)
            {
                alerts.Add(new GoalAlertResponse(g.Id, g.Name, $"Quedan solo {daysLeft} días para '{g.Name}' y faltan ${remaining:N0}.", "high"));
            }
            else if (g.CurrentAmount == 0 && daysLeft < 180)
            {
                alerts.Add(new GoalAlertResponse(g.Id, g.Name, $"Aún no has aportado a '{g.Name}'. ¡Empieza hoy con ${suggestedMonthly:N0}/mes!", "medium"));
            }
        }
        return alerts;
    }

    private GoalProgressResponse BuildProgress(SavingsGoal goal, IReadOnlyList<GoalContribution> contributions, decimal? monthlyAvailable)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var remaining = Math.Max(0, goal.TargetAmount - goal.CurrentAmount);
        var progressPercent = goal.TargetAmount > 0 ? Math.Min(100, Math.Round(goal.CurrentAmount / goal.TargetAmount * 100, 1)) : 0;
        var daysRemaining = Math.Max(0, goal.TargetDate.DayNumber - today.DayNumber);
        var monthsRemaining = Math.Max(1, ((goal.TargetDate.Year - today.Year) * 12) + goal.TargetDate.Month - today.Month);
        var suggestedMonthly = remaining > 0 ? Math.Ceiling(remaining / monthsRemaining * 100) / 100 : 0;

        DateOnly? projectedCompletion = null;
        if (contributions.Count > 0 && remaining > 0)
        {
            var firstContribution = contributions.MinBy(c => c.Date)?.Date ?? today;
            var daysSinceFirst = Math.Max(1, today.DayNumber - firstContribution.DayNumber);
            var monthsSinceFirst = Math.Max(1, daysSinceFirst / 30.0);
            var avgMonthlyContribution = (double)goal.CurrentAmount / monthsSinceFirst;
            if (avgMonthlyContribution > 0)
            {
                var monthsToGo = (int)Math.Ceiling((double)remaining / avgMonthlyContribution);
                projectedCompletion = today.AddMonths(monthsToGo);
            }
        }
        else if (remaining <= 0)
        {
            projectedCompletion = today;
        }

        var isOnTrack = remaining <= 0
            || (monthlyAvailable.HasValue && suggestedMonthly <= monthlyAvailable.Value)
            || (projectedCompletion.HasValue && projectedCompletion.Value <= goal.TargetDate);

        return new GoalProgressResponse(
            goal.Id, goal.Name, goal.Icon, goal.TargetAmount, goal.CurrentAmount,
            goal.TargetDate, goal.Status, goal.Priority,
            progressPercent, suggestedMonthly, projectedCompletion,
            daysRemaining, isOnTrack, monthlyAvailable,
            contributions, goal.CreatedAt, goal.UpdatedAt);
    }

    private async Task<decimal> ComputeAverageMonthlyExpensesAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var months = new List<string>(3);
        for (int i = 1; i <= 3; i++)
        {
            var m = today.AddMonths(-i);
            months.Add(m.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }

        decimal totalSpent = 0;
        int monthsWithData = 0;
        foreach (var mk in months)
        {
            var expenses = await expenseRepo.GetExpensesAsync(userId, mk, ct);
            if (expenses.Count > 0)
            {
                totalSpent += expenses.Sum(e => e.Amount);
                monthsWithData++;
            }
        }
        return monthsWithData > 0 ? totalSpent / monthsWithData : 0;
    }
}
