using System.Globalization;
using System.Net;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosUserProfileRepository(CosmosContainerProvider provider) : IUserProfileRepository
{
    public async Task<UserProfileSnapshot?> GetAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var rows = await CosmosHelper.QueryAsync<UserProfileRow>(
            provider.Settings,
            new QueryDefinition("SELECT TOP 1 c.id, c.userId, c.name, c.email, c.isPremium, c.isAdmin, c.monthlyIncome, c.updatedAt FROM c WHERE c.type = @type AND c.userId = @userId")
                .WithParameter("@type", "user-credential")
                .WithParameter("@userId", userId.Trim()),
            ct);

        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return new UserProfileSnapshot(
            row.UserId ?? string.Empty,
            row.Name ?? string.Empty,
            row.Email ?? string.Empty,
            row.IsPremium,
            row.IsAdmin,
            row.MonthlyIncome,
            CosmosHelper.ParseDateTimeOffset(row.UpdatedAt, DateTimeOffset.UtcNow));
    }

    public async Task<OperationResult> UpdateMonthlyIncomeAsync(string userId, decimal? monthlyIncome, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new OperationResult(false, "El usuario es requerido.");
        }

        var target = await ResolveUserCredentialIdentityAsync(userId.Trim(), ct);
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
        {
            return new OperationResult(false, "Usuario no encontrado.");
        }

        var patchOperations = new List<PatchOperation>
        {
            PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            PatchOperation.Set("/monthlyIncome", monthlyIncome)
        };

        try
        {
            await provider.Settings.PatchItemAsync<object>(
                target.Id,
                new PartitionKey(target.Id),
                patchOperations,
                cancellationToken: ct);

            return new OperationResult(true, monthlyIncome.HasValue
                ? "Ingreso mensual actualizado."
                : "Ingreso mensual eliminado.");
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

    private async Task<UserCredentialIdentity?> ResolveUserCredentialIdentityAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<UserCredentialIdentity>(
            provider.Settings,
            new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.type = @type AND c.userId = @userId")
                .WithParameter("@type", "user-credential")
                .WithParameter("@userId", userId),
            ct);

        return rows.FirstOrDefault();
    }

    private sealed class UserCredentialIdentity
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class UserProfileRow
    {
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public bool IsPremium { get; set; }
        public bool IsAdmin { get; set; }
        public decimal? MonthlyIncome { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
