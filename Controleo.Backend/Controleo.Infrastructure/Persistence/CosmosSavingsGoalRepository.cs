using System.Globalization;
using System.Net;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosSavingsGoalRepository(CosmosContainerProvider p) : ISavingsGoalRepository
{
    public async Task<IReadOnlyList<SavingsGoal>> GetGoalsAsync(string userId, CancellationToken ct)
    {
        var data = await CosmosHelper.QueryAsync<GoalDocument>(
            p.Goals,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid AND c.docType = 'goal'")
                .WithParameter("@uid", userId),
            ct);
        return data
            .Select(MapToEntity)
            .OrderByDescending(g => g.CreatedAt)
            .ToArray();
    }

    public async Task<SavingsGoal?> GetGoalByIdAsync(string userId, string goalId, CancellationToken ct)
    {
        try
        {
            var response = await p.Goals.ReadItemAsync<GoalDocument>(goalId, new PartitionKey(userId), cancellationToken: ct);
            var doc = response.Resource;
            if (doc is null || doc.DocType != "goal" || doc.UserId != userId) return null;
            return MapToEntity(doc);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<OperationResult> CreateGoalAsync(string userId, string name, string icon, decimal targetAmount, DateOnly targetDate, string priority, CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var doc = new GoalDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                DocType = "goal",
                Name = name.Trim(),
                Icon = icon.Trim(),
                TargetAmount = targetAmount,
                CurrentAmount = 0m,
                TargetDate = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Status = "Active",
                Priority = priority.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            await p.Goals.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);
            return new OperationResult(true, $"Meta creada (id: {doc.Id}).");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<OperationResult> UpdateGoalAsync(string userId, string goalId, string name, string icon, decimal targetAmount, DateOnly targetDate, string priority, string status, CancellationToken ct)
    {
        try
        {
            GoalDocument doc;
            try
            {
                var resp = await p.Goals.ReadItemAsync<GoalDocument>(goalId, new PartitionKey(userId), cancellationToken: ct);
                doc = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return new OperationResult(false, "Meta no encontrada.");
            }

            if (doc.UserId != userId) return new OperationResult(false, "Meta no encontrada.");

            doc.Name = name.Trim();
            doc.Icon = icon.Trim();
            doc.TargetAmount = targetAmount;
            doc.TargetDate = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            doc.Priority = priority.Trim();
            doc.Status = status.Trim();
            doc.UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await p.Goals.ReplaceItemAsync(doc, goalId, new PartitionKey(userId), cancellationToken: ct);
            return new OperationResult(true, "Meta actualizada.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<OperationResult> DeleteGoalAsync(string userId, string goalId, CancellationToken ct)
    {
        try
        {
            // Delete goal document
            try
            {
                await p.Goals.DeleteItemAsync<GoalDocument>(goalId, new PartitionKey(userId), cancellationToken: ct);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }

            // Delete all contributions for this goal
            var contributions = await CosmosHelper.QueryAsync<ContributionIdentity>(
                p.Goals,
                new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @uid AND c.docType = 'contribution' AND c.goalId = @gid")
                    .WithParameter("@uid", userId)
                    .WithParameter("@gid", goalId),
                ct);
            foreach (var c in contributions)
            {
                try { await p.Goals.DeleteItemAsync<GoalDocument>(c.Id, new PartitionKey(userId), cancellationToken: ct); }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            }

            return new OperationResult(true, "Meta eliminada.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<IReadOnlyList<GoalContribution>> GetContributionsAsync(string userId, string goalId, CancellationToken ct)
    {
        var data = await CosmosHelper.QueryAsync<ContributionDocument>(
            p.Goals,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid AND c.docType = 'contribution' AND c.goalId = @gid")
                .WithParameter("@uid", userId)
                .WithParameter("@gid", goalId),
            ct);
        return data
            .Select(d => new GoalContribution(
                d.Id,
                d.GoalId,
                d.Amount,
                DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : DateOnly.FromDateTime(DateTime.UtcNow),
                d.Note,
                CosmosHelper.ParseDateTimeOffset(d.CreatedAt, DateTimeOffset.UtcNow)))
            .OrderByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .ToArray();
    }

    public async Task<OperationResult> AddContributionAsync(string userId, string goalId, decimal amount, DateOnly date, string? note, CancellationToken ct)
    {
        try
        {
            // Verify goal exists
            var goal = await GetGoalByIdAsync(userId, goalId, ct);
            if (goal is null) return new OperationResult(false, "Meta no encontrada.");

            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var doc = new ContributionDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                DocType = "contribution",
                GoalId = goalId,
                Amount = amount,
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Note = note?.Trim(),
                CreatedAt = now
            };
            await p.Goals.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);

            // Update goal's current amount
            var newAmount = goal.CurrentAmount + amount;
            await UpdateGoalCurrentAmountAsync(userId, goalId, newAmount, ct);

            return new OperationResult(true, "Aporte registrado.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<OperationResult> DeleteContributionAsync(string userId, string goalId, string contributionId, CancellationToken ct)
    {
        try
        {
            // Read contribution to get the amount
            ContributionDocument contributionDoc;
            try
            {
                var resp = await p.Goals.ReadItemAsync<ContributionDocument>(contributionId, new PartitionKey(userId), cancellationToken: ct);
                contributionDoc = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return new OperationResult(false, "Aporte no encontrado.");
            }

            if (contributionDoc.UserId != userId || contributionDoc.GoalId != goalId)
                return new OperationResult(false, "Aporte no encontrado.");

            await p.Goals.DeleteItemAsync<ContributionDocument>(contributionId, new PartitionKey(userId), cancellationToken: ct);

            // Update goal's current amount
            var goal = await GetGoalByIdAsync(userId, goalId, ct);
            if (goal is not null)
            {
                var newAmount = Math.Max(0, goal.CurrentAmount - contributionDoc.Amount);
                await UpdateGoalCurrentAmountAsync(userId, goalId, newAmount, ct);
            }

            return new OperationResult(true, "Aporte eliminado.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<OperationResult> UpdateGoalCurrentAmountAsync(string userId, string goalId, decimal newCurrentAmount, CancellationToken ct)
    {
        try
        {
            GoalDocument doc;
            try
            {
                var resp = await p.Goals.ReadItemAsync<GoalDocument>(goalId, new PartitionKey(userId), cancellationToken: ct);
                doc = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return new OperationResult(false, "Meta no encontrada.");
            }

            doc.CurrentAmount = newCurrentAmount;
            doc.UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            if (newCurrentAmount >= doc.TargetAmount && doc.Status == "Active")
            {
                doc.Status = "Completed";
            }

            await p.Goals.ReplaceItemAsync(doc, goalId, new PartitionKey(userId), cancellationToken: ct);
            return new OperationResult(true, "Monto actualizado.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    private static SavingsGoal MapToEntity(GoalDocument d) => new(
        d.Id,
        d.Name,
        d.Icon,
        d.TargetAmount,
        d.CurrentAmount,
        DateOnly.TryParseExact(d.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var td) ? td : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
        d.Status,
        d.Priority,
        CosmosHelper.ParseDateTimeOffset(d.CreatedAt, DateTimeOffset.UtcNow),
        CosmosHelper.ParseDateTimeOffset(d.UpdatedAt, DateTimeOffset.UtcNow));

    private sealed record ContributionIdentity(string Id);
}

internal sealed class GoalDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DocType { get; set; } = "goal";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "🎯";
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public string TargetDate { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string Priority { get; set; } = "Media";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

internal sealed class ContributionDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DocType { get; set; } = "contribution";
    public string GoalId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Date { get; set; } = "";
    public string? Note { get; set; }
    public string CreatedAt { get; set; } = "";
}
