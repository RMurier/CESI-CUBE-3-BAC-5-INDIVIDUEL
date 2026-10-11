using Collector.Testing;
using Identity.Api.Infrastructure;

namespace Identity.Api.Tests;

public sealed class IdentityApiFactory : SqlServerWebApplicationFactory<Program, IdentityDbContext>;
