using System.Text.Json;
using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Messaging;
using MediatR;

namespace Cato.API.Services;

/// <summary>
/// Routes incoming RabbitMQ messages to the appropriate ingestion handler
/// based on the "source" field in the message payload.
/// </summary>
public class IngestionDispatcher : IIngestionDispatcher
{
    private readonly IMediator _mediator;
    private readonly ILogger<IngestionDispatcher> _logger;

    public IngestionDispatcher(IMediator mediator, ILogger<IngestionDispatcher> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task DispatchAsync(string messageJson, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<IngestionMessage>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (message is null)
            throw new InvalidOperationException("Could not deserialize ingestion message");

        _logger.LogInformation("Dispatching ingestion for source={Source}, appId={AppId}, file={FilePath}",
            message.Source, message.AppId, message.FilePath);

        // All per-app sources require appId; steam_special_events sends null.
        int RequireAppId() => message.AppId
            ?? throw new InvalidOperationException($"Source '{message.Source}' requires an appId but none was provided");

        switch (message.Source.ToLowerInvariant())
        {
            case "steam_financial":
            {
                await using var fs = File.OpenRead(message.FilePath);
                await _mediator.Send(new IngestFinancialDataCommand(RequireAppId(), Path.GetFileName(message.FilePath), fs), ct);
                break;
            }
            case "steamworks_wishlist":
            {
                await using var fs = File.OpenRead(message.FilePath);
                await _mediator.Send(new IngestWishlistDataCommand(RequireAppId(), Path.GetFileName(message.FilePath), fs), ct);
                break;
            }
            case "gamalytic_peak_ccu":
            {
                await using var fs = File.OpenRead(message.FilePath);
                await _mediator.Send(new IngestPeakCcuCommand(RequireAppId(), Path.GetFileName(message.FilePath), fs), ct);
                break;
            }
            case "steam_current_players":
                await _mediator.Send(new IngestCcuCommand(RequireAppId(), message.FilePath), ct);
                break;

            case "steamworks_owned_game":
            {
                await using var fs = File.OpenRead(message.FilePath);
                await _mediator.Send(new IngestOwnedGameDataCommand(Path.GetFileName(message.FilePath), fs), ct);
                break;
            }
            case "group_member_count":
                await _mediator.Send(new IngestGroupMemberCountCommand(RequireAppId(), message.FilePath), ct);
                break;

            case "steamdb_most_wished":
            case "steamdb_wishlist_activity":
                await _mediator.Send(new IngestSteamDbSnapshotCommand(RequireAppId(), message.FilePath), ct);
                break;

            case "steam_special_events":
                await _mediator.Send(new IngestSpecialEventsCommand(message.FilePath), ct);
                break;

            default:
                _logger.LogWarning("Unknown ingestion source: {Source}. Skipping.", message.Source);
                break;
        }
    }
}
