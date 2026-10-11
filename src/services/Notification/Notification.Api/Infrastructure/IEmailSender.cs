namespace Notification.Api.Infrastructure;

/// <summary>
/// Abstraction d'envoi d'email. Implémentation par défaut : log uniquement (fournisseur SMTP non
/// encore tranché — voir README section 12, "Fournisseur d'envoi d'email"). Remplacer
/// LoggingEmailSender par une vraie implémentation SMTP sans toucher au reste du service.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(Guid userId, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(Guid userId, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[EMAIL STUB] À user {UserId} — {Subject}: {Body}", userId, subject, body);
        return Task.CompletedTask;
    }
}
