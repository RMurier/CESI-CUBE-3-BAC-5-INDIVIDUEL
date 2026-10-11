using Catalog.Api.Infrastructure;
using Collector.Testing;

namespace Catalog.Api.Tests;

public sealed class CatalogApiFactory : SqlServerWebApplicationFactory<Program, CatalogDbContext>;
