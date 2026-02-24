using System.Text.Json;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services;

/// <summary>
/// Routes incoming RabbitMQ messages to the appropriate ingestion handler
/// based on the "source" field in the message payload.
/// </summary>
public class IngestionDispatcher
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

        switch (message.Source.ToLowerInvariant())
        {
            case "steam_financial":
                await _mediator.Send(new IngestFinancialDataCommand(message.AppId, message.FilePath), ct);
                break;

            case "steamworks_wishlist":
                await _mediator.Send(new IngestWishlistDataCommand(message.AppId, message.FilePath), ct);
                break;

            case "gamalytic_peak_ccu":
                await _mediator.Send(new IngestPeakCcuCommand(message.AppId, message.FilePath), ct);
                break;

            case "steamworks_owned_game":
                await _mediator.Send(new IngestOwnedGameDataCommand(message.AppId, message.FilePath), ct);
                break;

            default:
                _logger.LogWarning("Unknown ingestion source: {Source}. Skipping.", message.Source);
                break;
        }
    }
}
