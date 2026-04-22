namespace Controleo.Application.DTOs;
public sealed record ChatMessageRequest(string Content);
public sealed record ChatMessageResponse(string Role, string Content, DateTimeOffset CreatedAt);
public sealed record ChatHistoryResponse(IReadOnlyList<ChatMessageResponse> Messages);
public sealed record CoachSuggestionsResponse(IReadOnlyList<string> Suggestions);
