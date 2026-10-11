namespace Chat.Api.Domain;

/// <summary>
/// Une conversation = une paire (acheteur, vendeur) autour d'un article, identifiée côté client par
/// ConversationId (ex: "{articleId}:{buyerId}"). Pas de clé étrangère cross-service.
/// </summary>
public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ConversationId { get; init; }
    public required Guid SenderId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;
}
