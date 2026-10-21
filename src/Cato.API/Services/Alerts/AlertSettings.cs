namespace Cato.API.Services.Alerts;

/// <summary>The "Alerts" section. Off by default, so a local run never sends anything.</summary>
public sealed class AlertSettings
{
    public const string SectionName = "Alerts";

    /// <summary>Runs the alert monitor (worker role only) and the heartbeat metric.</summary>
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 60;

    public string AwsRegion { get; set; } = "eu-central-1";

    /// <summary>SNS topic for e-mail; empty disables the channel.</summary>
    public string? SnsTopicArn { get; set; }

    /// <summary>Secrets Manager id of the Discord webhook URL; empty disables the channel.</summary>
    public string? DiscordWebhookSecretId { get; set; }

    /// <summary>A webhook URL directly (local tests). Wins over the secret.</summary>
    public string? DiscordWebhookUrl { get; set; }

    /// <summary>Link in every message.</summary>
    public string JobsPageUrl { get; set; } = "https://catoptricgames.duckdns.org/jobs";

    /// <summary>A dispatch still pending this long after its slot, with its agent idle, is a missed run.</summary>
    public int MissedGraceMinutes { get; set; } = 5;

    public int AgentOfflineMinutes { get; set; } = 10;

    /// <summary>A queue with messages and no progress for this long is stuck.</summary>
    public int QueueStuckMinutes { get; set; } = 30;

    /// <summary>Sends CATO/WorkerHeartbeat to CloudWatch every cycle; an alarm watches it.</summary>
    public bool HeartbeatEnabled { get; set; }
}
