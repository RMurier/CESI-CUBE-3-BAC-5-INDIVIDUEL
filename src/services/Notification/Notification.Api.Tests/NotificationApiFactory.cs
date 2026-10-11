using Collector.Testing;
using Notification.Api.Infrastructure;

namespace Notification.Api.Tests;

public sealed class NotificationApiFactory : SqlServerWebApplicationFactory<Program, NotificationDbContext>;
