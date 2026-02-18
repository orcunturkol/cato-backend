namespace Cato.Domain.Entities;

public class GenreTag
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string TagType { get; set; } = "Genre"; // Genre, Subgenre, Mechanic, Theme, Mood
    public int Weight { get; set; }
    public string Source { get; set; } = "Steam"; // Steam, Internal, Manual
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
