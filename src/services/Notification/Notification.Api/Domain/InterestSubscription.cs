namespace Notification.Api.Domain;

/// <summary>Centre d'intérêt paramétré par un acheteur (ex: "figurine", "cassette V2000").</summary>
public sealed class InterestSubscription
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string Keyword { get; init; }
}
