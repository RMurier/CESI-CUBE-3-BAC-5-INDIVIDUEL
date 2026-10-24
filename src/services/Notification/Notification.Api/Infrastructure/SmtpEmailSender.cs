using System.Net;
using System.Net.Mail;

namespace Notification.Api.Infrastructure;

/// <summary>
/// Envoi SMTP réel (OVH en l'occurrence). L'adresse email n'est pas connue de Notification.Api (qui
/// ne possède que des <see cref="Guid"/> userId) : elle est résolue via l'endpoint interne
/// <c>GET /internal/users/{id}/email</c> d'Identity.Api (même mécanisme X-Maintenance-Key que les
/// autres appels inter-services pilotés par Airflow). Best-effort : un échec d'envoi (SMTP down,
/// Identity injoignable, utilisateur introuvable) est loggé mais ne doit jamais faire échouer le
/// traitement de l'évènement RabbitMQ qui a déclenché l'email.
/// </summary>
public sealed class SmtpEmailSender(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(Guid userId, string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var email = await ResolveEmailAsync(userId, cancellationToken);
            if (email is null)
            {
                logger.LogWarning("Email introuvable pour l'utilisateur {UserId} (Identity injoignable ou utilisateur supprimé) — envoi ignoré.", userId);
                return;
            }

            using var client = new SmtpClient(configuration["Smtp:Host"], configuration.GetValue("Smtp:Port", 587))
            {
                EnableSsl = configuration.GetValue("Smtp:EnableSsl", true),
                Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]),
            };
            using var message = new MailMessage(configuration["Smtp:From"]!, email, subject, body);
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Échec d'envoi email à l'utilisateur {UserId} — ignoré (best-effort, voir OmniaExceptionLogging pour les erreurs non catchées).", userId);
        }
    }

    private async Task<string?> ResolveEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("identity-internal");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{userId}/email");
        request.Headers.Add("X-Maintenance-Key", configuration["Maintenance:ApiKey"]);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<EmailResponse>(cancellationToken: cancellationToken);
        return payload?.Email;
    }

    private sealed record EmailResponse(string Email);
}
