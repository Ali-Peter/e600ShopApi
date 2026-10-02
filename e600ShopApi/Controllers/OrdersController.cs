using e600ShopApi.Data;
using e600ShopApi.Domain;
using e600ShopApi.Dtos;
using e600ShopApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace e600ShopApi.Controllers;

/// <summary>
/// Order placement. Guests check out with just their e-mail, so the customer row is
/// upserted by address — a Google sign-in with the same e-mail reuses it.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrdersController(
    AppDbContext database,
    IEmailService emailService,
    ILogger<OrdersController> logger) : ControllerBase
{
    // Mirrors FREE_SHIPPING_THRESHOLD / FLAT_SHIPPING_RATE in the Angular CartService so
    // the total written to PostgreSQL always matches the total the shopper saw at checkout.
    private const decimal FreeShippingThreshold = 75m;
    private const decimal FlatShippingRate = 6.95m;

    /// <summary>
    /// Places an order: resolves the customer, prices every line from the database,
    /// persists Order + OrderItem + Address, then sends the Mailgun confirmation.
    ///
    /// The e-mail is best-effort — it is sent only after the commit, and a Mailgun
    /// failure is logged rather than failing an order that is already saved.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Full name and e-mail are required." });
        }

        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            return BadRequest(new { message = "At least one item is required." });
        }

        var productIds = items.Select(item => item.ProductId).ToList();
        if (productIds.Count != productIds.Distinct().Count())
        {
            return BadRequest(new { message = "Each product may appear only once in an order." });
        }

        var products = await database.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        if (productIds.Any(id => products.All(product => product.Id != id)))
        {
            return BadRequest(new { message = "One or more products in your cart are no longer available." });
        }

        // --- customer ---------------------------------------------------------
        var normalizedEmail = email.ToLowerInvariant();
        var (firstName, lastName) = SplitName(fullName);

        var user = await database.Users
            .FirstOrDefaultAsync(existing => existing.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                FirstName = firstName,
                LastName = lastName,
                CreatedAt = DateTime.UtcNow,
            };
            database.Users.Add(user);
        }

        // --- order, priced entirely server-side -------------------------------
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        var lines = new List<OrderEmailLine>();
        var subtotal = 0m;

        foreach (var item in items)
        {
            var product = products.Single(existing => existing.Id == item.ProductId);
            var lineSubtotal = product.Price * item.Quantity;
            subtotal += lineSubtotal;

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                Subtotal = lineSubtotal,
            });

            lines.Add(new OrderEmailLine(product.Name, item.Quantity, product.Price, lineSubtotal));
        }

        var shipping = subtotal == 0m || subtotal >= FreeShippingThreshold ? 0m : FlatShippingRate;
        order.TotalAmount = subtotal + shipping;

        // --- shipping address -------------------------------------------------
        // Address.State is NOT NULL but the checkout form has no state field yet,
        // so the city doubles as the state until one is added. The schema has no
        // postal-code or notes column either, so those stay client-side for now.
        var state = string.IsNullOrWhiteSpace(request.State) ? request.City : request.State;

        database.Addresses.Add(new Address
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FullName = fullName,
            AddressLine = request.Address?.Trim() ?? string.Empty,
            City = request.City?.Trim() ?? string.Empty,
            State = state?.Trim() ?? string.Empty,
            Country = request.Country?.Trim() ?? string.Empty,
            PhoneNumber = request.Phone?.Trim() ?? string.Empty,
        });

        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);

        var orderNumber = FormatOrderNumber(order.Id);

        // --- confirmation e-mail (best-effort, never rolls back the order) -----
        var emailSent = false;

        try
        {
            await emailService.SendOrderConfirmationAsync(
                new OrderConfirmationEmail(
                    normalizedEmail,
                    string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName,
                    orderNumber,
                    lines,
                    subtotal,
                    shipping,
                    order.TotalAmount,
                    order.CreatedAt),
                cancellationToken);

            emailSent = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Order {OrderId} was saved but the confirmation e-mail to {Email} failed.",
                order.Id,
                normalizedEmail);
        }

        var response = new OrderResponse(
            order.Id,
            orderNumber,
            order.TotalAmount,
            order.Items.Count,
            order.Status.ToString(),
            order.CreatedAt,
            emailSent);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, response);
    }

    /// <summary>
    /// Returns one order with its line items. The id is an unguessable GUID and the
    /// rest of the API is unauthenticated, so this stays open for demo/verification.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailResponse>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .Include(existing => existing.Items)
            .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        var detail = new OrderDetailResponse(
            order.Id,
            FormatOrderNumber(order.Id),
            order.TotalAmount,
            order.Status.ToString(),
            order.CreatedAt,
            order.Items
                .Select(item => new OrderLineResponse(
                    item.Product.Name,
                    item.Quantity,
                    item.UnitPrice,
                    item.Subtotal))
                .ToList());

        return Ok(detail);
    }

    /// <summary>Human-readable reference derived from the primary key (no extra column needed).</summary>
    private static string FormatOrderNumber(Guid orderId) =>
        $"E6-{orderId.ToString("N")[..8].ToUpperInvariant()}";

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var separator = fullName.IndexOf(' ');

        return separator <= 0
            ? (fullName, string.Empty)
            : (fullName[..separator].Trim(), fullName[(separator + 1)..].Trim());
    }
}