namespace Cato.Infrastructure.Messaging;

public class RabbitMqSettings
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Optional <c>amqp://</c> or <c>amqps://user:pass@host:5671/vhost</c>. When set, it
    /// overrides <see cref="HostName"/> and <see cref="Port"/>, and the user and password
    /// when it carries them (URL-encode special characters). <c>amqps</c> turns TLS on,
    /// which Amazon MQ requires.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>The connection name the broker shows; Program.cs sets <c>cato-{role}</c>.</summary>
    public string ClientProvidedName { get; set; } = "cato-backend";

    public string QueueName { get; set; } = "cato-ingestion";
    public string QueueNameV2 { get; set; } = "cato-ingestion-v2";
    public string GameAnalyzedQueue { get; set; } = "cato-game-analyzed";
    public string ExchangeName { get; set; } = "cato-data";
    public string DeadLetterExchange { get; set; } = "cato-data-dlx";
    public string DeadLetterQueue { get; set; } = "cato-ingestion-dlq";
}
