using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface IChatRepository
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string userId, int limit, CancellationToken ct);
    Task SaveMessageAsync(string userId, string role, string content, CancellationToken ct);
    Task ClearHistoryAsync(string userId, CancellationToken ct);
}
