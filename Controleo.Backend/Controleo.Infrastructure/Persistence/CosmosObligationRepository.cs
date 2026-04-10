using System.Globalization;
using System.Net;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosObligationRepository(CosmosContainerProvider p) : IObligationRepository
{
    private const string ObligationDocType = "obligation";

    public async Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(string userId, CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid AND c.docType = @docType")
            .WithParameter("@uid", userId)
            .WithParameter("@docType", ObligationDocType);

        var data = await CosmosHelper.QueryAsync<ObligationDocument>(p.Recurring, query, ct);
        return data
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .Select(Map)
            .OrderBy(item => item.DueDayOfMonth)
            .ThenBy(item => item.Description)
            .ToArray();
    }

    public async Task<PagedObligationResult> GetObligationsPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var normalizedPageSize = pageSize is 10 or 20 ? pageSize : 5;
        var all = await GetObligationsAsync(userId, ct);

        var totalCount = all.Count;
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);
        var safePage = Math.Min(Math.Max(pageNumber, 1), totalPages);
        var items = all.Skip((safePage - 1) * normalizedPageSize).Take(normalizedPageSize).ToArray();

        return new PagedObligationResult(
            items,
            safePage,
            normalizedPageSize,
            totalCount,
            totalPages,
            safePage > 1,
            safePage < totalPages);
    }

    public async Task<OperationResult> UpsertObligationAsync(string userId, string? id, ObligationUpsertData data, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var docId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();

        try
        {
            var existing = (await CosmosHelper.QueryAsync<ObligationDocument>(
                p.Recurring,
                new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id AND c.userId = @uid AND c.docType = @docType")
                    .WithParameter("@id", docId)
                    .WithParameter("@uid", userId)
                    .WithParameter("@docType", ObligationDocType),
                ct)).FirstOrDefault();

            var doc = new ObligationDocument
            {
                Id = docId,
                UserId = userId,
                DocType = ObligationDocType,
                Description = data.Description,
                MovementType = data.MovementType,
                PaymentMethod = data.PaymentMethod,
                DueDayOfMonth = data.DueDayOfMonth,
                MonthlyPayment = data.MonthlyPayment,
                ReminderDaysBefore = data.ReminderDaysBefore,
                StartMonth = data.StartMonth,
                IsActive = data.IsActive,
                CreatedAt = existing?.CreatedAt ?? now.ToString("O", CultureInfo.InvariantCulture),
                UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
            };

            await p.Recurring.UpsertItemAsync(doc, new PartitionKey(doc.UserId), cancellationToken: ct);
            return new OperationResult(true, "Obligación guardada.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteObligationAsync(string userId, string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new OperationResult(false, "El id es requerido.");

        try
        {
            var query = new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.id = @id AND c.userId = @uid AND c.docType = @docType")
                .WithParameter("@id", id.Trim())
                .WithParameter("@uid", userId)
                .WithParameter("@docType", ObligationDocType);

            var existing = await CosmosHelper.QueryAsync<ExpenseIdentity>(p.Recurring, query, ct);
            if (existing.Count == 0)
                return new OperationResult(false, "No existe.");

            await p.Recurring.DeleteItemAsync<ObligationDocument>(id.Trim(), new PartitionKey(userId), cancellationToken: ct);
            return new OperationResult(true, "Eliminado.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new OperationResult(false, "No existe.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    private static ObligationItem Map(ObligationDocument doc)
    {
        return new ObligationItem(
            doc.Id,
            doc.Description ?? string.Empty,
            doc.MovementType ?? string.Empty,
            doc.PaymentMethod ?? string.Empty,
            doc.DueDayOfMonth,
            doc.MonthlyPayment,
            doc.ReminderDaysBefore,
            string.IsNullOrWhiteSpace(doc.StartMonth) ? DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture) : doc.StartMonth,
            doc.IsActive,
            CosmosHelper.ParseDateTimeOffset(doc.CreatedAt, DateTimeOffset.UtcNow),
            CosmosHelper.ParseDateTimeOffset(doc.UpdatedAt, DateTimeOffset.UtcNow));
    }
}