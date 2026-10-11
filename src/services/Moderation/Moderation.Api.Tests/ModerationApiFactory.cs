using Collector.Testing;
using Moderation.Api.Infrastructure;

namespace Moderation.Api.Tests;

public sealed class ModerationApiFactory : SqlServerWebApplicationFactory<Program, ModerationDbContext>;
