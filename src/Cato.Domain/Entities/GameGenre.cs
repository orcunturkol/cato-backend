namespace Cato.Domain.Entities;

public class GameGenre
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string GenreName { get; set; } = string.Empty;
    public string GenreType { get; set; } = "Primary"; // Primary, Secondary
    public string Source { get; set; } = "Steam"; // Steam, Internal
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
