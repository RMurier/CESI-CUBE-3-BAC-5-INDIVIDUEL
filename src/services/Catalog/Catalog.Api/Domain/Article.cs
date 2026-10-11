namespace Catalog.Api.Domain;

public enum ArticleStatus
{
    PendingReview,
    Published,
    Rejected,
}

public sealed class Article
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ShopId { get; init; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required decimal Price { get; set; }
    public required decimal ShippingCost { get; set; }
    public ArticleStatus Status { get; set; } = ArticleStatus.PendingReview;
    public string? RejectionReason { get; set; }
    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
}
