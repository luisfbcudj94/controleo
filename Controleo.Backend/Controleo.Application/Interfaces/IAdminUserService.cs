using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;

namespace Controleo.Application.Interfaces;

public interface IAdminUserService
{
    Task<PagedAdminUsersResult> GetUsersPageAsync(string? searchTerm, int pageNumber, int pageSize, CancellationToken ct);
    Task<OperationResult> UpdateUserAccessAsync(string actorUserId, string targetUserId, AdminUserUpdateRequest request, CancellationToken ct);
    Task<OperationResult> DeleteUserAndDataAsync(string actorUserId, string targetUserId, CancellationToken ct);
    Task<OperationResult> LogImpersonationEventAsync(string actorUserId, string targetUserId, string eventType, bool isSuccess, string? detail, CancellationToken ct);
}