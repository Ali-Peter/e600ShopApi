namespace e600ShopApi.Services;

/// <summary>
/// A single priced line rendered into an order-confirmation e-mail.
/// </summary>
public sealed record OrderEmailLine(
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal);

/// <summary>
/// Everything the e-mail layer needs to render a confirmation. Deliberately a plain
/// data record so the Mailgun service never has to reach back into the database.
/// </summary>
public sealed record OrderConfirmationEmail(
    string RecipientEmail,
    string RecipientFirstName,
    string OrderNumber,
    IReadOnlyList<OrderEmailLine> Lines,
    decimal Subtotal,
    decimal Shipping,
    decimal TotalAmount,
    DateTime PlacedAtUtc);

/// <summary>
/// Sends transactional e-mail. Mailgun is the only implementation; the interface keeps
/// the order controller ignorant of the provider and lets tests substitute a stub so
/// no test ever opens a socket or needs credentials.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends the confirmation. Implementations throw on failure; the caller decides
    /// whether a failure is fatal (an order never is — it is already committed).
    /// </summary>
    Task SendOrderConfirmationAsync(
        OrderConfirmationEmail email,
        CancellationToken cancellationToken = default);
}