namespace Moderation.Api.Domain;

/// <summary>Trace d'audit de chaque décision de contrôle automatisé prise sur un article soumis.</summary>
public sealed class ModerationDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ArticleId { get; init; }
    public required bool Approved { get; init; }
    public string? RejectionReason { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}
