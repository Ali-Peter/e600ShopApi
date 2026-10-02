using System.ComponentModel.DataAnnotations;

namespace e600ShopApi.Dtos;

/// <summary>
/// Request payload for creating and updating products. Deliberately excludes Id and
/// CreatedAt so clients cannot forge server-managed values, and enforces the same
/// limits as the database columns so invalid input never reaches PostgreSQL.
/// </summary>
public class ProductRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; set; }

    [StringLength(2048)]
    public string? ImageUrl { get; set; }

    [Required]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
}
