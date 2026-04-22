using System.Globalization;
using System.Net;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosChatRepository(CosmosContainerProvider provider) : IChatRepository
{
    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string userId, int limit, CancellationToken ct)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        var data = await CosmosHelper.QueryAsync<ChatDocument>(
            provider.Settings,
            new QueryDefinition("SELECT * FROM c WHERE c.type = 'chat-message' AND c.userId = @uid ORDER BY c.createdAt DESC")
                .WithParameter("@uid", userId),
            ct);
        return data
            .OrderByDescending(d => d.CreatedAt)
            .Take(safeLimit)
            .Select(d => new ChatMessage(
                d.Id,
                d.UserId,
                d.Role,
                d.Content,
                CosmosHelper.ParseDateTimeOffset(d.CreatedAt, DateTimeOffset.UtcNow)))
            .Reverse()
            .ToArray();
    }

    public async Task SaveMessageAsync(string userId, string role, string content, CancellationToken ct)
    {
        var doc = new ChatDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "chat-message",
            UserId = userId,
            Role = role,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        await provider.Settings.CreateItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public async Task ClearHistoryAsync(string userId, CancellationToken ct)
    {
        var docs = await CosmosHelper.QueryAsync<ChatIdentity>(
            provider.Settings,
            new QueryDefinition("SELECT c.id FROM c WHERE c.type = 'chat-message' AND c.userId = @uid")
                .WithParameter("@uid", userId),
            ct);
        foreach (var d in docs)
        {
            try { await provider.Settings.DeleteItemAsync<ChatDocument>(d.Id, new PartitionKey(d.Id), cancellationToken: ct); }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
        }
    }

    private sealed record ChatIdentity(string Id);
}

internal sealed class ChatDocument
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "chat-message";
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
