namespace Controleo.Mobile.Core.Models;

public sealed record ChatMessageItem(string Role, string Content, DateTimeOffset CreatedAt)
{
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
}

public sealed record ChatHistoryResult(IReadOnlyList<ChatMessageItem> Messages);

public sealed record CoachSuggestionsResult(IReadOnlyList<string> Suggestions);
