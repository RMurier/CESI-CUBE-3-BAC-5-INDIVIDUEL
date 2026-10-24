using System.Net;
using System.Net.Http.Json;
using Collector.Contracts.Events;
using Identity.Api.Domain;
using Identity.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Api.Tests;

[Trait("Category", "Integration")]
public sealed class EmailConfirmationTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Registering_publishes_an_email_confirmation_requested_event()
    {
        string email = $"{Guid.NewGuid()}@test.collector.shop";

        HttpResponseMessage response = await factory.CreateClient().PostAsJsonAsync(
            "/auth/register", new { Email = email, Password = "Password1!", DisplayName = "Test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Contains(factory.EventBus.PublishedOf<EmailConfirmationRequestedEvent>(), e => e.Email == email);
    }

    [Fact]
    public async Task Confirming_with_a_valid_token_marks_the_user_as_confirmed()
    {
        string email = $"{Guid.NewGuid()}@test.collector.shop";
        await factory.CreateClient().PostAsJsonAsync(
            "/auth/register", new { Email = email, Password = "Password1!", DisplayName = "Test" });
        EmailConfirmationRequestedEvent requested = factory.EventBus.PublishedOf<EmailConfirmationRequestedEvent>().Single(e => e.Email == email);

        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/auth/confirm-email?token={requested.ConfirmationToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using IServiceScope scope = factory.Services.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.True(user.IsEmailConfirmed);
    }

    [Fact]
    public async Task Confirming_with_an_unknown_token_is_rejected()
    {
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/auth/confirm-email?token=does-not-exist");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
