namespace Controleo.Application.DTOs;
public sealed record GoalUpsertRequest(string Name, string Icon, decimal TargetAmount, DateOnly TargetDate, string Priority, string? Status = null);
