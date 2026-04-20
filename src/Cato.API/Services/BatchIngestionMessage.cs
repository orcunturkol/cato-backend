using System.Text.Json;

namespace Cato.API.Services;

public record BatchIngestionMessage
{
    public Guid BatchId { get; init; }
    public DateTimeOffset ScrapedAt { get; init; }
    public int SchemaVersion { get; init; }
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<BatchIngestionItem> Items { get; init; } = Array.Empty<BatchIngestionItem>();
}

public record BatchIngestionItem
{
    public int AppId { get; init; }
    public DateTimeOffset ScrapedAt { get; init; }
    public JsonElement Data { get; init; }
}
