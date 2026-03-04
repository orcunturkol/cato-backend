namespace Cato.Domain.Entities;

public class LegalEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // Developer, Publisher
    public string? ContactEmail { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Game> DeveloperGames { get; set; } = [];
    public ICollection<Game> PublisherGames { get; set; } = [];
}
