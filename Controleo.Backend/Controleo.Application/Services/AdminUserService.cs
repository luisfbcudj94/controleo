using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;

namespace Controleo.Application.Services;

public sealed class AdminUserService(IAdminUserRepository repository) : IAdminUserService
{
    public Task<PagedAdminUsersResult> GetUsersPageAsync(string? searchTerm, int pageNumber, int pageSize, CancellationToken ct)
    {
        var safePageNumber = Math.Max(pageNumber, 1);
        var safePageSize = pageSize is 10 or 20 ? pageSize : 5;
        return repository.GetUsersPageAsync(searchTerm, safePageNumber, safePageSize, ct);
    }

    public Task<OperationResult> UpdateUserAccessAsync(string actorUserId, string targetUserId, AdminUserUpdateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return Task.FromResult(new OperationResult(false, "El usuario es requerido."));

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal) && !request.IsAdmin)
            return Task.FromResult(new OperationResult(false, "No puedes quitarte permisos de administrador."));

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal) && request.IsDisabled)
            return Task.FromResult(new OperationResult(false, "No puedes inhabilitar tu propia cuenta."));

        var isPremium = request.IsPremium || request.IsAdmin;
        return repository.UpdateUserAccessAsync(actorUserId, targetUserId, isPremium, request.IsAdmin, request.IsDisabled, ct);
    }

    public Task<OperationResult> DeleteUserAndDataAsync(string actorUserId, string targetUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return Task.FromResult(new OperationResult(false, "El usuario es requerido."));

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
            return Task.FromResult(new OperationResult(false, "No puedes eliminar tu propia cuenta."));

        return repository.DeleteUserAndDataAsync(actorUserId, targetUserId, ct);
    }

    public Task<OperationResult> LogImpersonationEventAsync(string actorUserId, string targetUserId, string eventType, bool isSuccess, string? detail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Task.FromResult(new OperationResult(false, "El actor es requerido."));

        if (string.IsNullOrWhiteSpace(targetUserId))
            return Task.FromResult(new OperationResult(false, "El usuario objetivo es requerido."));

        var normalizedEventType = (eventType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedEventType))
            return Task.FromResult(new OperationResult(false, "El tipo de evento es requerido."));

        return repository.LogImpersonationEventAsync(actorUserId, targetUserId, normalizedEventType, isSuccess, detail, ct);
    }
}