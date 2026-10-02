using System.ComponentModel.DataAnnotations;

namespace e600ShopApi.Dtos;

/// <summary>One line of the cart as submitted by the checkout form.</summary>
public class CreateOrderItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Mirrors the quantity cap enforced by the Angular CartService.</summary>
    [Range(1, 99)]
    public int Quantity { get; set; }
}

/// <summary>
/// Checkout payload.
///
/// Deliberately excludes prices and totals: every monetary value is recomputed from
/// the database so a tampered client cannot choose its own price. Postal code and
/// delivery notes are collected by the form but the current schema has no columns for
/// them, so they are accepted and ignored rather than persisted (the extra JSON
/// properties are simply dropped during binding).
/// </summary>
public class CreateOrderRequest
{
    [Required]
    [StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// The checkout form has no state field yet, so this is optional — when it is
    /// absent the API falls back to <see cref="City"/> because Address.State is NOT NULL.
    /// </summary>
    [StringLength(100)]
    public string? State { get; set; }

    [Required]
    [StringLength(100)]
    public string Country { get; set; } = string.Empty;

    public List<CreateOrderItemRequest>? Items { get; set; }
}