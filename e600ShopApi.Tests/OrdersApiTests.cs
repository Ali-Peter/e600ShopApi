using System.Net;
using System.Net.Http.Json;
using e600ShopApi.Data;
using e600ShopApi.Domain;
using e600ShopApi.Dtos;
using e600ShopApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace e600ShopApi.Tests;

/// <summary>
/// End-to-end tests for POST /api/Orders, exercised through the real HTTP pipeline
/// (routing, [ApiController] validation, status codes, JSON serialisation) with an
/// in-memory database and a stubbed <see cref="IEmailService"/>. This proves the
/// confirmation e-mail is composed with the right recipient, reference and totals
/// without ever contacting Mailgun.
/// </summary>
public class OrdersApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    private readonly HttpClient client;

    public OrdersApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    // ------------------------------------------------------------------ helpers

    private sealed record CreatedOrder(
        Guid Id,
        string OrderNumber,
        decimal TotalAmount,
        int ItemCount,
        string Status,
        DateTime CreatedAt,
        bool EmailSent);

    private async Task<Product> SeedProductAsync(decimal price = 19.99m)
    {
        var product = new Product
        {
            Name = $"Seed {Guid.NewGuid():N}",
            Description = "Seeded product for order tests",
            Price = price,
            Category = "electronics",
            StockQuantity = 50,
            CreatedAt = DateTime.UtcNow,
        };

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Products.Add(product);
        await database.SaveChangesAsync();

        return product;
    }

    /// <summary>
    /// Mirrors what the Angular checkout actually posts: no prices, no state field,
    /// postal code / notes present (the API drops them — there are no columns).
    /// </summary>
    private static object BuildPayload(
        Product product,
        int quantity = 2,
        string email = "ada@example.com",
        string fullName = "Ada Obi") => new
    {
        fullName,
        email,
        phone = "+234 801 234 5678",
        address = "12 Admiralty Way, Lekki Phase 1",
        city = "Lagos",
        country = "Nigeria",
        postalCode = "106104",
        notes = "Leave with the gate guard.",
        items = new[] { new { productId = product.Id, quantity } },
    };

    private RecordingEmailService Recorder =>
        factory.Services.GetRequiredService<RecordingEmailService>();

    /// <summary>Reads the persisted order straight out of the database.</summary>
    private async Task<Order?> LoadOrderAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.Orders
            .Include(existing => existing.Items)
            .FirstOrDefaultAsync(existing => existing.Id == id);
    }

    /// <summary>Loads the shipping address written for the given customer.</summary>
    private async Task<List<Address>> LoadAddressesAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.Addresses
            .Where(existing => existing.UserId == userId)
            .ToListAsync();
    }

    // --------------------------------------------------------------- POST /api/Orders

    [Fact]
    public async Task CreateOrder_Valid_PersistsOrderAndItems_AndSendsConfirmation()
    {
        var product = await SeedProductAsync(price: 19.99m);

        var response = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product, quantity: 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>();
        Assert.NotNull(created);
        Assert.Equal("Pending", created.Status);
        Assert.True(created.EmailSent);
        Assert.Equal(1, created.ItemCount);
        // 3 x 19.99 = 59.97, under the 75.00 free-shipping threshold → +6.95.
        Assert.Equal(66.92m, created.TotalAmount);

        // --- persisted -----------------------------------------------------
        var persisted = await LoadOrderAsync(created.Id);
        Assert.NotNull(persisted);
        var item = Assert.Single(persisted.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(19.99m, item.UnitPrice);
        Assert.Equal(59.97m, item.Subtotal);
        Assert.Equal(created.TotalAmount, persisted.TotalAmount);
        Assert.Equal(product.Id, item.ProductId);

        // The checkout form has no state field, so the city doubles as the state.
        // Addresses hang off the customer, who may have ordered before in this suite.
        var addresses = await LoadAddressesAsync(persisted.UserId);
        Assert.Contains(addresses, address =>
            address.City == "Lagos" &&
            address.State == "Lagos" &&
            address.FullName == "Ada Obi");
    }

    [Fact]
    public async Task CreateOrder_ConfirmationCarriesRecipientReferenceAndTotals()
    {
        var product = await SeedProductAsync(price: 19.99m);

        var response = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product));
        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>();
        Assert.NotNull(created);

        // The recorder is shared across the fixture, so scope to this order.
        var confirmation = Recorder.Sent.SingleOrDefault(mail => mail.OrderNumber == created.OrderNumber);

        Assert.NotNull(confirmation);
        Assert.Equal("ada@example.com", confirmation.RecipientEmail);
        Assert.Equal("Ada", confirmation.RecipientFirstName);
        Assert.Single(confirmation.Lines);
        // 2 x 19.99 = 39.98, under the 75.00 free-shipping threshold → +6.95.
        Assert.Equal(39.98m, confirmation.Subtotal);
        Assert.Equal(6.95m, confirmation.Shipping);
        Assert.Equal(46.93m, confirmation.TotalAmount);
    }

    [Fact]
    public async Task CreateOrder_AboveFreeShippingThreshold_ChargesNoShipping()
    {
        var product = await SeedProductAsync(price: 100m);

        var response = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product, quantity: 1));
        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>();

        Assert.NotNull(created);
        Assert.Equal(100m, created.TotalAmount);
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_Returns400_AndPersistsNothing()
    {
        var before = await CountOrdersAsync();

        var payload = new
        {
            fullName = "Ada Obi",
            email = "ada@example.com",
            phone = "+234 801 234 5678",
            address = "12 Admiralty Way",
            city = "Lagos",
            country = "Nigeria",
            items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } },
        };

        var response = await client.PostAsJsonAsync("/api/Orders", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await CountOrdersAsync());
    }

    [Fact]
    public async Task CreateOrder_EmptyBasket_Returns400()
    {
        var payload = new
        {
            fullName = "Ada Obi",
            email = "ada@example.com",
            phone = "+234 801 234 5678",
            address = "12 Admiralty Way",
            city = "Lagos",
            country = "Nigeria",
            items = Array.Empty<object>(),
        };

        var response = await client.PostAsJsonAsync("/api/Orders", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_MalformedEmail_Returns400()
    {
        var product = await SeedProductAsync();

        var response = await client.PostAsJsonAsync(
            "/api/Orders",
            BuildPayload(product, email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_SecondOrderForTheSameCustomer_ReusesTheUserRow()
    {
        var product = await SeedProductAsync();

        var first = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product));
        var second = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customers = await database.Users
            .Where(existing => existing.Email == "ada@example.com")
            .CountAsync();

        Assert.Equal(1, customers);
    }

    [Fact]
    public async Task CreateOrder_WhenEmailFails_StillReturns201_AndPersistsTheOrder()
    {
        // Re-run the pipeline with a sender that always blows up: the order must
        // survive, because it is committed before the confirmation is sent.
        using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IEmailService));
                services.AddSingleton<IEmailService, ExplodingEmailService>();
            }));

        var failingClient = failingFactory.CreateClient();
        var product = await SeedProductAsync(price: 25m);

        var response = await failingClient.PostAsJsonAsync("/api/Orders", BuildPayload(product));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>();
        Assert.NotNull(created);
        Assert.False(created.EmailSent);

        Assert.NotNull(await LoadOrderAsync(created.Id));
    }

    // -------------------------------------------------------------- GET /api/Orders

    [Fact]
    public async Task GetOrder_KnownId_ReturnsOkWithLines()
    {
        var product = await SeedProductAsync(price: 10m);
        var createdResponse = await client.PostAsJsonAsync("/api/Orders", BuildPayload(product));
        var created = await createdResponse.Content.ReadFromJsonAsync<CreatedOrder>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/Orders/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(created.OrderNumber, detail.OrderNumber);
        Assert.Single(detail.Lines);
        Assert.Equal("Seed", detail.Lines[0].ProductName.Split(' ')[0]);
    }

    [Fact]
    public async Task GetOrder_UnknownId_Returns404()
    {
        var response = await client.GetAsync($"/api/Orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<int> CountOrdersAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Orders.CountAsync();
    }

    private sealed class ExplodingEmailService : IEmailService
    {
        public Task SendOrderConfirmationAsync(
            OrderConfirmationEmail email,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Mail transport failure during the test.");
    }
}
