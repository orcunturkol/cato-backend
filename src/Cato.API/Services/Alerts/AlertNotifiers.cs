using System.Net.Http.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Alerts;

/// <param name="Subject">One line, ASCII, for the e-mail subject.</param>
/// <param name="Body">Plain text with line breaks.</param>
public sealed record AlertMessage(string Subject, string Body);

/// <summary>One channel. <see cref="SendAsync"/> throws when the message did not go out.</summary>
public interface IAlertNotifier
{
    /// <summary>Short and stable: stored in alert_incident to remember what was sent.</summary>
    string Name { get; }

    Task SendAsync(AlertMessage message, CancellationToken ct);
}

/// <summary>E-mail through the SNS topic (the instance role may publish to it).</summary>
public sealed class SnsAlertNotifier : IAlertNotifier
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly string _topicArn;

    public SnsAlertNotifier(IAmazonSimpleNotificationService sns, IOptions<AlertSettings> settings)
    {
        _sns = sns;
        _topicArn = settings.Value.SnsTopicArn!;
    }

    public string Name => "sns";

    public Task SendAsync(AlertMessage message, CancellationToken ct) =>
        _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _topicArn,
            // SNS: ASCII, no line breaks, under 100 characters.
            Subject = AlertText.Clip(AlertText.Ascii(message.Subject), 99, "..."),
            Message = message.Body,
        }, ct);
}

/// <summary>A Discord channel webhook. The URL is a secret, read once from Secrets Manager.</summary>
public sealed class DiscordAlertNotifier : IAlertNotifier
{
    private readonly HttpClient _http;
    private readonly IAmazonSecretsManager? _secrets;
    private readonly AlertSettings _settings;
    private readonly SemaphoreSlim _urlLock = new(1, 1);
    private string? _url;

    public DiscordAlertNotifier(HttpClient http, IOptions<AlertSettings> settings, IAmazonSecretsManager? secrets = null)
    {
        _http = http;
        _settings = settings.Value;
        _secrets = secrets;
        _url = string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl) ? null : _settings.DiscordWebhookUrl;
    }

    public string Name => "discord";

    public async Task SendAsync(AlertMessage message, CancellationToken ct)
    {
        var url = await WebhookUrlAsync(ct);
        // Discord limits content to 2000 characters.
        using var response = await _http.PostAsJsonAsync(url, new { content = AlertText.Clip(message.Body, 1990) }, ct);
        // Never log the URL: it is the secret.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Discord webhook answered {(int)response.StatusCode}");
    }

    private async Task<string> WebhookUrlAsync(CancellationToken ct)
    {
        if (_url is not null)
            return _url;
        await _urlLock.WaitAsync(ct);
        try
        {
            if (_url is null)
            {
                if (_secrets is null)
                    throw new InvalidOperationException("No Discord webhook URL and no Secrets Manager client.");
                var secret = await _secrets.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = _settings.DiscordWebhookSecretId }, ct);
                _url = secret.SecretString.Trim();
            }
            return _url;
        }
        finally
        {
            _urlLock.Release();
        }
    }
}

public static class AlertText
{
    public static string Clip(string text, int max, string ellipsis = "…") =>
        text.Length <= max ? text : text[..(max - ellipsis.Length)] + ellipsis;

    /// <summary>Printable ASCII on one line; other characters become "?".</summary>
    public static string Ascii(string text) =>
        new(text.Select(c => c is >= ' ' and <= '~' ? c : c is '\n' or '\r' or '\t' ? ' ' : '?').ToArray());
}
