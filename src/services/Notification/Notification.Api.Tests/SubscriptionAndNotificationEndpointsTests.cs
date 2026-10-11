using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;
using Xunit;

namespace Notification.Api.Tests;

public sealed class SubscriptionAndNotificationEndpointsTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_buyer_can_subscribe_to_a_keyword()
    {
        var userId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync("/subscriptions", new { UserId = userId, Keyword = "figurine" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var subscription = await response.Content.ReadFromJsonAsync<InterestSubscription>();
        Assert.Equal("figurine", subscription!.Keyword);
    }

    [Fact]
    public async Task A_user_can_list_their_notifications_and_mark_one_as_read()
    {
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.Add(new UserNotification { UserId = userId, Message = "Nouvel article : figurine Star Wars" });
            await db.SaveChangesAsync();
        }

        var list = await _client.GetFromJsonAsync<List<UserNotification>>($"/notifications/{userId}");
        Assert.Single(list!);
        Assert.False(list![0].Read);

        var readResponse = await _client.PostAsync($"/notifications/{list[0].Id}/read", content: null);
        Assert.Equal(HttpStatusCode.NoContent, readResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var updated = await verifyDb.Notifications.AsNoTracking().SingleAsync(n => n.Id == list[0].Id);
        Assert.True(updated.Read);
    }
}
