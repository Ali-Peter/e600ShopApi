using System.Net;
using System.Net.Http.Json;
using e600ShopApi.Data;
using e600ShopApi.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace e600ShopApi.Tests;

/// <summary>
/// End-to-end tests for the Product API, exercised through the real HTTP pipeline
/// (routing, [ApiController] validation, status codes, JSON serialisation) with the
/// database replaced by an isolated in-memory store.
/// </summary>
public class ProductsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    private readonly HttpClient client;

    public ProductsApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Product> SeedProductAsync(
        string? name = null,
        string? category = null,
        decimal price = 19.99m,
        int stockQuantity = 5)
    {
        var product = new Product
        {
            Name = name ?? $"Seed {Guid.NewGuid():N}",
            Description = "Seeded product for integration tests",
            Price = price,
            Category = category ?? "electronics",
            StockQuantity = stockQuantity,
            CreatedAt = DateTime.UtcNow,
        };

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Products.Add(product);
        await database.SaveChangesAsync();

        return product;
    }

    // --------------------------------------------------------------------- GET

    [Fact]
    public async Task GetProducts_ReturnsOk_AndIncludesSeededProduct()
    {
        var seeded = await SeedProductAsync();

        var response = await client.GetAsync("/api/Products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        Assert.NotNull(products);
        Assert.Contains(products, existing => existing.Id == seeded.Id);
    }

    [Fact]
    public async Task GetProducts_UnknownCategory_ReturnsOkWithEmptyList()
    {
        var response = await client.GetAsync($"/api/Products?category=zzz-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        Assert.NotNull(products);
        Assert.Empty(products);
    }

    [Fact]
    public async Task GetProduct_KnownId_ReturnsOkWithProduct()
    {
        var seeded = await SeedProductAsync(name: "Detailed product");

        var response = await client.GetAsync($"/api/Products/{seeded.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(product);
        Assert.Equal(seeded.Id, product.Id);
        Assert.Equal("Detailed product", product.Name);
    }

    [Fact]
    public async Task GetProduct_UnknownId_Returns404()
    {
        var response = await client.GetAsync($"/api/Products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --------------------------------------------------------------------- POST

    [Fact]
    public async Task PostProduct_Valid_Returns201_WithLocation_AndPersists()
    {
        var payload = new
        {
            name = "Aurora Desk Lamp",
            description = "Warm dimmable LED desk lamp",
            price = 49.95,
            imageUrl = (string?)null,
            category = "home",
            stockQuantity = 12,
        };

        var response = await client.PostAsJsonAsync("/api/Products", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Aurora Desk Lamp", created.Name);
        Assert.Equal("home", created.Category);
        Assert.Equal(49.95m, created.Price);
        Assert.Equal(12, created.StockQuantity);

        // Location must point at the GET-by-id endpoint for the new resource.
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/Products/{created.Id}", response.Headers.Location.ToString());

        // The resource must be retrievable after creation.
        var fetched = await client.GetAsync($"/api/Products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var fetchedProduct = await fetched.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(fetchedProduct);
        Assert.Equal(created.Name, fetchedProduct.Name);
    }

    [Fact]
    public async Task PostProduct_MissingName_Returns400()
    {
        var payload = new { category = "electronics", price = 10, stockQuantity = 1 };

        var response = await client.PostAsJsonAsync("/api/Products", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostProduct_MissingCategory_Returns400()
    {
        var payload = new { name = "No category", price = 10, stockQuantity = 1 };

        var response = await client.PostAsJsonAsync("/api/Products", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostProduct_NegativePrice_Returns400()
    {
        var payload = new
        {
            name = "Negative price",
            category = "electronics",
            price = -1.0,
            stockQuantity = 1,
        };

        var response = await client.PostAsJsonAsync("/api/Products", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostProduct_NegativeStock_Returns400()
    {
        var payload = new
        {
            name = "Negative stock",
            category = "electronics",
            price = 10,
            stockQuantity = -5,
        };

        var response = await client.PostAsJsonAsync("/api/Products", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------- PUT

    [Fact]
    public async Task PutProduct_Valid_Returns200_UpdatesFields_PreservesIdAndCreatedAt()
    {
        var seeded = await SeedProductAsync(name: "Original name", category: "sports");

        var payload = new
        {
            name = "Updated name",
            description = "Updated description",
            price = 74.50,
            imageUrl = (string?)null,
            category = "beauty",
            stockQuantity = 3,
        };

        var response = await client.PutAsJsonAsync($"/api/Products/{seeded.Id}", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(updated);
        Assert.Equal(seeded.Id, updated.Id);
        Assert.Equal(seeded.CreatedAt, updated.CreatedAt);
        Assert.Equal("Updated name", updated.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.Equal(74.50m, updated.Price);
        Assert.Equal("beauty", updated.Category);
        Assert.Equal(3, updated.StockQuantity);

        // The changes must be persisted, not just echoed back.
        var fetched = await client.GetAsync($"/api/Products/{seeded.Id}");
        var fetchedProduct = await fetched.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(fetchedProduct);
        Assert.Equal("Updated name", fetchedProduct.Name);
        Assert.Equal(74.50m, fetchedProduct.Price);
    }

    [Fact]
    public async Task PutProduct_UnknownId_Returns404()
    {
        var payload = new { name = "Ghost", category = "electronics", price = 1, stockQuantity = 1 };

        var response = await client.PutAsJsonAsync($"/api/Products/{Guid.NewGuid()}", payload);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutProduct_NegativePrice_Returns400()
    {
        var seeded = await SeedProductAsync();

        var payload = new
        {
            name = "Invalid update",
            category = "electronics",
            price = -9.99,
            stockQuantity = 1,
        };

        var response = await client.PutAsJsonAsync($"/api/Products/{seeded.Id}", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------- DELETE

    [Fact]
    public async Task DeleteProduct_Existing_Returns204_AndRemoves()
    {
        var seeded = await SeedProductAsync();

        var response = await client.DeleteAsync($"/api/Products/{seeded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await client.GetAsync($"/api/Products/{seeded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_UnknownId_Returns404()
    {
        var response = await client.DeleteAsync($"/api/Products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_ReferencedByOrderItem_Returns409_AndKeepsProduct()
    {
        var seeded = await SeedProductAsync();

        // Build a minimal order that references the product.
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User
            {
                Email = $"buyer-{Guid.NewGuid():N}@example.com",
                FirstName = "Test",
                LastName = "Buyer",
            };
            database.Users.Add(user);
            await database.SaveChangesAsync();

            var order = new Order
            {
                UserId = user.Id,
                TotalAmount = 19.99m,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };
            database.Orders.Add(order);
            await database.SaveChangesAsync();

            database.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = seeded.Id,
                Quantity = 1,
                UnitPrice = 19.99m,
                Subtotal = 19.99m,
            });
            await database.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/Products/{seeded.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // The product must still exist after the rejected delete.
        var fetched = await client.GetAsync($"/api/Products/{seeded.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }
}
