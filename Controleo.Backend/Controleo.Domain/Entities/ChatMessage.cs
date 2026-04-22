namespace Controleo.Domain.Entities;
public sealed record ChatMessage(string Id, string UserId, string Role, string Content, DateTimeOffset CreatedAt);
