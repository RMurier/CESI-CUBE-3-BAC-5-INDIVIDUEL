using Collector.Contracts.Events;
using Collector.Messaging;
using Collector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Api.Messaging;
using Xunit;

namespace Notification.Api.Tests;

[Trait("Category", "Integration")]
public sealed class EmailConfirmationRequestedConsumerTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Handling_the_event_does_not_throw_even_without_a_real_SMTP_server()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        EmailConfirmationRequestedConsumer consumer = new(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EmailConfirmationRequestedConsumer>.Instance);
        EmailConfirmationRequestedEvent requested = new(
            Guid.NewGuid(), "buyer@example.com", "opaque-token", DateTimeOffset.UtcNow.AddHours(48));

        await ConsumerTestHelper.HandleAsync(consumer, requested, scope.ServiceProvider);
    }
}
