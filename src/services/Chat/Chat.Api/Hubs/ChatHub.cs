using System.Security.Claims;
using Chat.Api.Domain;
using Chat.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Hubs;

[Authorize]
public sealed class ChatHub(ChatDbContext db) : Hub
{
    public async Task JoinConversation(string conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
    }

    public async Task SendMessage(string conversationId, string content)
    {
        var senderId = Guid.Parse(Context.User!.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
        var sanitized = PersonalDataFilter.Sanitize(content);

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderId = senderId,
            Content = sanitized,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        await Clients.Group(conversationId).SendAsync("MessageReceived", new
        {
            message.Id,
            message.ConversationId,
            message.SenderId,
            message.Content,
            message.SentAt,
        });
    }
}
