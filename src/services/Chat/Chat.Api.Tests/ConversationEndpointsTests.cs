using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Chat.Api.Domain;
using Chat.Api.Infrastructure;
using Collector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chat.Api.Tests;

public sealed class ConversationEndpointsTests(ChatApiFactory factory) : IClassFixture<ChatApiFactory>
{
    [Fact]
    public async Task Authenticated_user_can_read_the_history_of_a_conversation()
    {
        const string conversationId = "article-42:buyer-1";
        var senderId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            db.Messages.Add(new ChatMessage { ConversationId = conversationId, SenderId = senderId, Content = "Toujours disponible ?" });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.IssueToken(senderId, "Buyer"));

        var response = await client.GetAsync($"/conversations/{conversationId}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await response.Content.ReadFromJsonAsync<List<ChatMessage>>();
        Assert.Single(messages!);
        Assert.Equal("Toujours disponible ?", messages![0].Content);
    }

    [Fact]
    public async Task Reading_a_conversation_without_a_token_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/conversations/any/messages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
