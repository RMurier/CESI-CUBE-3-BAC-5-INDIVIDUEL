namespace Catalog.Api.Contracts;

public sealed record CreateShopRequest(string Name, string? Description);
public sealed record CreateArticleRequest(string Title, string Description, decimal Price, decimal ShippingCost, Guid CategoryId);
public sealed record CreateCategoryRequest(string Name);
public sealed record UpdatePriceRequest(decimal NewPrice);
public sealed record CreateOrderRequest(Guid ArticleId);
