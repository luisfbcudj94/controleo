using Controleo.Domain.Common;
using Controleo.Domain.Entities;

namespace Controleo.Domain.Interfaces;

public interface IUserProfileRepository
{
    Task<UserProfileSnapshot?> GetAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateMonthlyIncomeAsync(string userId, decimal? monthlyIncome, CancellationToken ct);
}
