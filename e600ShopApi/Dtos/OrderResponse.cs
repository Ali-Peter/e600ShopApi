namespace e600ShopApi.Dtos;

/// <summary>
/// What the checkout UI needs after a successful order: the reference it displays,
/// the authoritative total that was written to the database, and whether the Mailgun
/// confirmation actually left the building.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    decimal TotalAmount,
    int ItemCount,
    string Status,
    DateTime CreatedAt,
    bool EmailSent);

/// <summary>A priced line as stored on the order.</summary>
public sealed record OrderLineResponse(
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal);

/// <summary>Full representation returned by GET /api/Orders/{id}.</summary>
public sealed record OrderDetailResponse(
    Guid Id,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<OrderLineResponse> Lines);