using Controleo.Application.DTOs;
using Controleo.Domain.Common;
namespace Controleo.Application.Interfaces;
public interface ISavingsGoalService
{
    Task<IReadOnlyList<GoalProgressResponse>> GetAllGoalsAsync(string userId, CancellationToken ct);
    Task<GoalProgressResponse?> GetGoalProgressAsync(string userId, string goalId, CancellationToken ct);
    Task<OperationResult> CreateGoalAsync(string userId, GoalUpsertRequest request, CancellationToken ct);
    Task<OperationResult> UpdateGoalAsync(string userId, string goalId, GoalUpsertRequest request, CancellationToken ct);
    Task<OperationResult> DeleteGoalAsync(string userId, string goalId, CancellationToken ct);
    Task<OperationResult> AddContributionAsync(string userId, string goalId, GoalContributionRequest request, CancellationToken ct);
    Task<OperationResult> DeleteContributionAsync(string userId, string goalId, string contributionId, CancellationToken ct);
    Task<GoalSimulationResponse> SimulateScenarioAsync(string userId, string goalId, GoalSimulationRequest request, CancellationToken ct);
    Task<IReadOnlyList<GoalAlertResponse>> GetGoalAlertsAsync(string userId, CancellationToken ct);
}
