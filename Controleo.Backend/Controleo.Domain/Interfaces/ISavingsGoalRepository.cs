using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface ISavingsGoalRepository
{
    Task<IReadOnlyList<SavingsGoal>> GetGoalsAsync(string userId, CancellationToken ct);
    Task<SavingsGoal?> GetGoalByIdAsync(string userId, string goalId, CancellationToken ct);
    Task<OperationResult> CreateGoalAsync(string userId, string name, string icon, decimal targetAmount, DateOnly targetDate, string priority, CancellationToken ct);
    Task<OperationResult> UpdateGoalAsync(string userId, string goalId, string name, string icon, decimal targetAmount, DateOnly targetDate, string priority, string status, CancellationToken ct);
    Task<OperationResult> DeleteGoalAsync(string userId, string goalId, CancellationToken ct);
    Task<IReadOnlyList<GoalContribution>> GetContributionsAsync(string userId, string goalId, CancellationToken ct);
    Task<OperationResult> AddContributionAsync(string userId, string goalId, decimal amount, DateOnly date, string? note, CancellationToken ct);
    Task<OperationResult> DeleteContributionAsync(string userId, string goalId, string contributionId, CancellationToken ct);
    Task<OperationResult> UpdateGoalCurrentAmountAsync(string userId, string goalId, decimal newCurrentAmount, CancellationToken ct);
}
