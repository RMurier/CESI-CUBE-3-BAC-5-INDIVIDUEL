namespace Collector.Messaging;

/// <summary>
/// Configuration RabbitMQ, lue depuis la section "RabbitMq" de la config (env vars / K8s Secret
/// injecté par Infisical : RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, ...).
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Exchange topic unique partagé par tous les services Collector.</summary>
    public string ExchangeName { get; set; } = "collector.events";
}
