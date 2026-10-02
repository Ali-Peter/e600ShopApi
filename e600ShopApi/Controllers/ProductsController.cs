using e600ShopApi.Data;
using e600ShopApi.Domain;
using e600ShopApi.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace e600ShopApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(AppDbContext database) : ControllerBase
{
    /// <summary>Lists products, optionally filtered by category and/or free-text search.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Product>>> GetProducts(
        [FromQuery] string? category,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = database.Products;

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(product => product.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(product =>
                product.Name.Contains(term) || product.Description.Contains(term));
        }

        var products = await query.OrderBy(product => product.Name).ToListAsync(cancellationToken);
        return Ok(products);
    }

    /// <summary>Returns a single product by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Creates a product and returns it with a Location header.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Product>> CreateProduct(
        ProductRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var category = request.Category?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
        {
            return BadRequest(new { message = "Name and category are required." });
        }

        var product = new Product
        {
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            Price = request.Price,
            ImageUrl = request.ImageUrl,
            Category = category,
            StockQuantity = request.StockQuantity,
            CreatedAt = DateTime.UtcNow,
        };

        await database.Products.AddAsync(product, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    /// <summary>Updates an existing product and returns the updated representation.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> UpdateProduct(
        Guid id,
        ProductRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var category = request.Category?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
        {
            return BadRequest(new { message = "Name and category are required." });
        }

        var product = await database.Products
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        product.Name = name;
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.Price = request.Price;
        product.ImageUrl = request.ImageUrl;
        product.Category = category;
        product.StockQuantity = request.StockQuantity;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The product was deleted by another request after it was read.
            return NotFound();
        }

        return Ok(product);
    }

    /// <summary>Deletes a product that is not referenced by any order.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var isReferencedByOrders = await database.OrderItems
            .AnyAsync(item => item.ProductId == id, cancellationToken);

        if (isReferencedByOrders)
        {
            return Conflict(new
            {
                message = "The product cannot be deleted because it is referenced by existing orders.",
            });
        }

        database.Products.Remove(product);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound();
        }

        return NoContent();
    }
}
