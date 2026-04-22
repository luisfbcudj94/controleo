using Controleo.Application.DTOs;
using Controleo.Domain.Common;
namespace Controleo.Application.Interfaces;
public interface IMoneyCoachService
{
    Task<ChatMessageResponse> SendMessageAsync(string userId, string content, CancellationToken ct);
    Task<ChatHistoryResponse> GetHistoryAsync(string userId, int limit, CancellationToken ct);
    Task<OperationResult> ClearHistoryAsync(string userId, CancellationToken ct);
    Task<CoachSuggestionsResponse> GetSuggestionsAsync(string userId, CancellationToken ct);
}
