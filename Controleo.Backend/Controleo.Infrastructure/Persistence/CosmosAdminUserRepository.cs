using System.Globalization;
using System.Net;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosAdminUserRepository(CosmosContainerProvider provider, IOptions<LocalAuthOptions> authOptions) : IAdminUserRepository
{
    private readonly LocalAuthOptions _authOptions = authOptions.Value;

    public async Task<PagedAdminUsersResult> GetUsersPageAsync(string? searchTerm, int pageNumber, int pageSize, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<UserCredentialRow>(
            provider.Settings,
            new QueryDefinition("SELECT c.id, c.userId, c.name, c.email, c.isPremium, c.isAdmin, c.isDisabled, c.createdAt, c.updatedAt, c.lastLoginAt FROM c WHERE c.type = @type")
                .WithParameter("@type", "user-credential"),
            ct);

        IEnumerable<UserCredentialRow> filtered = rows;
        var term = (searchTerm ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = filtered.Where(static (row) => row is not null)
                .Where(row =>
                    (!string.IsNullOrWhiteSpace(row.Email) && row.Email.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(row.Name) && row.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var ordered = filtered
            .OrderByDescending(row => CosmosHelper.ParseDateTimeOffset(row.LastLoginAt, DateTimeOffset.MinValue))
            .ThenBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalCount = ordered.Length;
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var safePageNumber = Math.Min(Math.Max(pageNumber, 1), totalPages);
        var skip = (safePageNumber - 1) * pageSize;

        var items = ordered
            .Skip(skip)
            .Take(pageSize)
            .Select(Map)
            .ToArray();

        return new PagedAdminUsersResult(
            items,
            safePageNumber,
            pageSize,
            totalCount,
            totalPages,
            safePageNumber > 1,
            safePageNumber < totalPages);
    }

    public async Task<OperationResult> UpdateUserAccessAsync(string actorUserId, string targetUserId, bool isPremium, bool isAdmin, bool isDisabled, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return new OperationResult(false, "El usuario es requerido.");

        var target = await GetUserCredentialAsync(targetUserId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
            return new OperationResult(false, "Usuario no encontrado.");

        var isSuperAdmin = IsSuperAdminEmail(target.Email);
        if (isSuperAdmin && isDisabled)
            return new OperationResult(false, "No se puede inhabilitar al super-admin.");

        var effectiveIsAdmin = isSuperAdmin || isAdmin;
        var effectiveIsPremium = isSuperAdmin || isPremium || effectiveIsAdmin;
        var effectiveIsDisabled = !isSuperAdmin && isDisabled;

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal) && !effectiveIsAdmin)
            return new OperationResult(false, "No puedes quitarte permisos de administrador.");

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal) && effectiveIsDisabled)
            return new OperationResult(false, "No puedes inhabilitar tu propia cuenta.");

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var patchOps = new List<PatchOperation>
        {
            PatchOperation.Set("/isAdmin", effectiveIsAdmin),
            PatchOperation.Set("/isPremium", effectiveIsPremium),
            PatchOperation.Set("/isDisabled", effectiveIsDisabled),
            PatchOperation.Set("/updatedAt", now)
        };

        try
        {
            await provider.Settings.PatchItemAsync<UserCredentialIdentity>(target.Id, new PartitionKey(target.Id), patchOps, cancellationToken: ct);
            return new OperationResult(true, "Usuario actualizado.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new OperationResult(false, "Usuario no encontrado.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteUserAndDataAsync(string actorUserId, string targetUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return new OperationResult(false, "El usuario es requerido.");

        var target = await GetUserCredentialAsync(targetUserId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
            return new OperationResult(false, "Usuario no encontrado.");

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
            return new OperationResult(false, "No puedes eliminar tu propia cuenta.");

        if (IsSuperAdminEmail(target.Email))
            return new OperationResult(false, "No se puede eliminar al super-admin.");

        try
        {
            var deletedExpenses = await DeleteExpensesByUserAsync(targetUserId, ct);
            var deletedBudgets = await DeleteBudgetsByUserAsync(targetUserId, ct);
            var deletedRecurring = await DeleteRecurringByUserAsync(targetUserId, ct);
            var deletedSettings = await DeleteSettingsByUserAsync(targetUserId, ct);

            if (await TryDeleteSettingsAsync(target.Id, ct))
                deletedSettings++;

            var catalogDocId = BuildCatalogDocId(targetUserId);
            if (await TryDeleteSettingsAsync(catalogDocId, ct))
                deletedSettings++;

            var totalDeleted = deletedExpenses + deletedBudgets + deletedRecurring + deletedSettings;
            return new OperationResult(true, $"Usuario y datos eliminados ({totalDeleted} documentos).");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error eliminando usuario: {ex.Message}");
        }
    }

    public async Task<OperationResult> LogImpersonationEventAsync(string actorUserId, string targetUserId, string eventType, bool isSuccess, string? detail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            return new OperationResult(false, "El actor es requerido.");

        if (string.IsNullOrWhiteSpace(targetUserId))
            return new OperationResult(false, "El usuario objetivo es requerido.");

        if (string.IsNullOrWhiteSpace(eventType))
            return new OperationResult(false, "El evento es requerido.");

        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var doc = new ImpersonationAuditDocument
            {
                Id = $"impersonation-{Guid.NewGuid():N}",
                Type = "impersonation-audit",
                ActorUserId = actorUserId.Trim(),
                TargetUserId = targetUserId.Trim(),
                EventType = eventType.Trim(),
                IsSuccess = isSuccess,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            await provider.Settings.CreateItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
            return new OperationResult(true, "Evento de suplantación registrado.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error registrando auditoría: {ex.Message}");
        }
    }

    private AdminUserItem Map(UserCredentialRow row)
    {
        var email = row.Email ?? string.Empty;
        var isSuperAdmin = IsSuperAdminEmail(email);
        var isAdmin = row.IsAdmin || isSuperAdmin;
        var isPremium = row.IsPremium || isAdmin;
        var isDisabled = !isSuperAdmin && row.IsDisabled;

        return new AdminUserItem(
            row.UserId ?? string.Empty,
            row.Name ?? string.Empty,
            email,
            isPremium,
            isAdmin,
            isSuperAdmin,
            isDisabled,
            row.CreatedAt ?? string.Empty,
            row.UpdatedAt ?? string.Empty,
            row.LastLoginAt ?? string.Empty);
    }

    private async Task<UserCredentialIdentity?> GetUserCredentialAsync(string userId, CancellationToken ct)
    {
        var targetRows = await CosmosHelper.QueryAsync<UserCredentialIdentity>(
            provider.Settings,
            new QueryDefinition("SELECT TOP 1 c.id, c.userId, c.email FROM c WHERE c.type = @type AND c.userId = @userId")
                .WithParameter("@type", "user-credential")
                .WithParameter("@userId", userId),
            ct);

        return targetRows.FirstOrDefault();
    }

    private async Task<int> DeleteExpensesByUserAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<ExpenseDeleteRow>(
            provider.Expenses,
            new QueryDefinition("SELECT c.id, c.monthKey FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            ct);

        var deleted = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.MonthKey))
                continue;

            try
            {
                await provider.Expenses.DeleteItemAsync<ExpenseDocument>(row.Id, new PartitionKey(row.MonthKey), cancellationToken: ct);
                deleted++;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return deleted;
    }

    private async Task<int> DeleteBudgetsByUserAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<BudgetDeleteRow>(
            provider.Budgets,
            new QueryDefinition("SELECT c.id, c.movementType FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            ct);

        var deleted = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.MovementType))
                continue;

            try
            {
                await provider.Budgets.DeleteItemAsync<BudgetDocument>(row.Id, new PartitionKey(row.MovementType), cancellationToken: ct);
                deleted++;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return deleted;
    }

    private async Task<int> DeleteRecurringByUserAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<RecurringDeleteRow>(
            provider.Recurring,
            new QueryDefinition("SELECT c.id, c.userId FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            ct);

        var deleted = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                continue;

            var partition = string.IsNullOrWhiteSpace(row.UserId) ? userId : row.UserId;
            var removed = await TryDeleteRecurringAsync(row.Id, partition!, ct);

            if (!removed)
            {
                removed = await TryDeleteRecurringAsync(row.Id, row.Id, ct);
            }

            if (removed)
                deleted++;
        }

        return deleted;
    }

    private async Task<int> DeleteSettingsByUserAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<SettingsDeleteRow>(
            provider.Settings,
            new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            ct);

        var deleted = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                continue;

            if (await TryDeleteSettingsAsync(row.Id, ct))
                deleted++;
        }

        return deleted;
    }

    private async Task<bool> TryDeleteRecurringAsync(string id, string partitionKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(partitionKey))
            return false;

        try
        {
            await provider.Recurring.DeleteItemAsync<RecurringExpenseDocument>(id, new PartitionKey(partitionKey), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> TryDeleteSettingsAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        try
        {
            await provider.Settings.DeleteItemAsync<object>(id, new PartitionKey(id), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string BuildCatalogDocId(string userId)
        => $"{userId.Trim().Replace("/", "_").ToLowerInvariant()}-catalogs";

    private bool IsSuperAdminEmail(string? email)
        => string.Equals(NormalizeEmail(email), NormalizeEmail(_authOptions.SuperAdminEmail), StringComparison.Ordinal);

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private sealed class UserCredentialRow
    {
        public string? Id { get; set; }
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public bool IsPremium { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsDisabled { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public string? LastLoginAt { get; set; }
    }

    private sealed class UserCredentialIdentity
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    private sealed class ExpenseDeleteRow
    {
        public string? Id { get; set; }
        public string? MonthKey { get; set; }
    }

    private sealed class BudgetDeleteRow
    {
        public string? Id { get; set; }
        public string? MovementType { get; set; }
    }

    private sealed class RecurringDeleteRow
    {
        public string? Id { get; set; }
        public string? UserId { get; set; }
    }

    private sealed class SettingsDeleteRow
    {
        public string? Id { get; set; }
    }

    private sealed class ImpersonationAuditDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ActorUserId { get; set; } = string.Empty;
        public string TargetUserId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? Detail { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}