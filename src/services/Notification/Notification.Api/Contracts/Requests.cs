namespace Notification.Api.Contracts;

public sealed record CreateSubscriptionRequest(Guid UserId, string Keyword);
