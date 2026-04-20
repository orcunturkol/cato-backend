namespace Cato.Infrastructure.Messaging;

public class RabbitMqSettings
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string QueueName { get; set; } = "cato-ingestion";
    public string QueueNameV2 { get; set; } = "cato-ingestion-v2";
    public string ExchangeName { get; set; } = "cato-data";
    public string DeadLetterExchange { get; set; } = "cato-data-dlx";
    public string DeadLetterQueue { get; set; } = "cato-ingestion-dlq";
}
