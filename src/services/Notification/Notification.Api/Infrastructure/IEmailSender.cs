namespace Notification.Api.Infrastructure;

/// <summary>
/// Abstraction d'envoi d'email. <see cref="SmtpEmailSender"/> est utilisé dès que `Smtp:Host` est
/// configuré (secrets Infisical `Smtp__*`, SMTP OVH) ; sinon <see cref="LoggingEmailSender"/> prend
/// le relais (même pattern que Stripe/Omnia : secret absent = fonctionnalité désactivée sans erreur,
/// voir Program.cs).
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
