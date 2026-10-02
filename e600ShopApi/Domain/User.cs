namespace e600ShopApi.Domain;

public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Subject identifier from Google OAuth, set once Google authentication is enabled.</summary>
    public string? GoogleSubjectId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = new List<Order>();

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
}
