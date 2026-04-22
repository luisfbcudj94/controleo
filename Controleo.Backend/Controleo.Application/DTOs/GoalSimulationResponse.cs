namespace Controleo.Application.DTOs;
public sealed record GoalSimulationResponse(DateOnly? ProjectedDate, decimal RequiredMonthlyAmount, string Feasibility, string Description);
