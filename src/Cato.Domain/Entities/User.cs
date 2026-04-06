using System.ComponentModel.DataAnnotations;

namespace Cato.Domain.Entities;

/// <summary>
/// User aggregate root representing a user in the system.
/// This is the main aggregate that encapsulates all related entities and business logic.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public ICollection<UserProfile> Profiles { get; set; } = [];
}