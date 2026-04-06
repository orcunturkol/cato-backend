namespace Cato.Domain.Entities;

/// <summary>
/// Represents a user's profile information within the User aggregate.
/// This entity is part of the User aggregate and cannot exist without its parent.
/// </summary>
public class UserProfile
{
    public Guid Id { get; set; }
    
    public Guid UserId { get; set; }
    
    public string? Bio { get; set; }
    
    public string? AvatarUrl { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public User User { get; set; } = null!;
}