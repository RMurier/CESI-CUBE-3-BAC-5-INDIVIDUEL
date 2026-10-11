using Chat.Api.Infrastructure;
using Collector.Testing;

namespace Chat.Api.Tests;

public sealed class ChatApiFactory : SqlServerWebApplicationFactory<Program, ChatDbContext>;
