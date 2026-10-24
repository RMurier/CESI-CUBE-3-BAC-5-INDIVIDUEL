using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.Extensions.Options;
using Notification.Api.Infrastructure;

namespace Notification.Api.Messaging;

/// <summary>Envoie l'email de confirmation d'inscription (lien vers GET /auth/confirm-email côté Identity).</summary>
public sealed class EmailConfirmationRequestedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailConfirmationRequestedConsumer> logger)
    : RabbitMqConsumer<EmailConfirmationRequestedEvent>(options, "notification.email-confirmation-requested", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        EmailConfirmationRequestedEvent request,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        IEmailSender emailSender = scopedServices.GetRequiredService<IEmailSender>();
        IConfiguration configuration = scopedServices.GetRequiredService<IConfiguration>();

        // Identity:PublicBaseUrl (externe, cliquable depuis un email) — distinct d'Identity:InternalBaseUrl
        // (DNS interne au cluster, utilisé par SmtpEmailSender pour résoudre un userId -> email).
        string baseUrl = configuration["Identity:PublicBaseUrl"]
            ?? throw new InvalidOperationException("Configuration 'Identity:PublicBaseUrl' manquante.");
        string link = $"{baseUrl.TrimEnd('/')}/auth/confirm-email?token={Uri.EscapeDataString(request.ConfirmationToken)}";

        string body = $"""
            Bienvenue sur Collector.shop !

            Confirmez votre adresse email en cliquant sur ce lien (valide jusqu'au {request.ExpiresAt:dd/MM/yyyy HH:mm} UTC) :
            {link}

            Si vous n'êtes pas à l'origine de cette inscription, ignorez cet email.
            """;

        await emailSender.SendAsync(request.UserId, "Confirmez votre adresse email - Collector.shop", body, cancellationToken);
    }
}
