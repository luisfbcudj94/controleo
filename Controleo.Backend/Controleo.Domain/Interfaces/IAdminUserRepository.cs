using Controleo.Domain.Common;
using Controleo.Domain.Entities;

namespace Controleo.Domain.Interfaces;

public interface IAdminUserRepository
{
    Task<PagedAdminUsersResult> GetUsersPageAsync(string? searchTerm, int pageNumber, int pageSize, CancellationToken ct);
    Task<OperationResult> UpdateUserAccessAsync(string actorUserId, string targetUserId, bool isPremium, bool isAdmin, bool isDisabled, CancellationToken ct);
    Task<OperationResult> DeleteUserAndDataAsync(string actorUserId, string targetUserId, CancellationToken ct);
    Task<OperationResult> LogImpersonationEventAsync(string actorUserId, string targetUserId, string eventType, bool isSuccess, string? detail, CancellationToken ct);
}