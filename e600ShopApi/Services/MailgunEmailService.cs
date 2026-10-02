using System.Net.Http.Headers;
using System.Text;

namespace e600ShopApi.Services;

/// <summary>
/// Sends order confirmations through the Mailgun HTTP Messages API:
///
///   POST {BaseUrl}/v3/{Domain}/messages
///   Authorization: Basic base64("api:" + private-api-key)
///   Content-Type: multipart/form-data with from, to, subject, text and html
///   (EU accounts use https://api.eu.mailgun.net instead of https://api.mailgun.net)
///
/// Every setting is read from configuration — user-secrets in development,
/// Mailgun__* environment variables in production. The private key is never written
/// to source and can never reach the Angular client, which has no path to this class.
/// </summary>
public sealed class MailgunEmailService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<MailgunEmailService> logger) : IEmailService
{
    /// <summary>Named HttpClient registered in Program.cs so the timeout is shared.</summary>
    public const string HttpClientName = "Mailgun";

    private const string DefaultBaseUrl = "https://api.mailgun.net";

    public async Task SendOrderConfirmationAsync(
        OrderConfirmationEmail email,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Mailgun:ApiKey"];
        var domain = configuration["Mailgun:Domain"];
        var fromAddress = configuration["Mailgun:FromAddress"];
        var baseUrl = configuration["Mailgun:BaseUrl"];

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(domain) ||
            string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException(
                "Mailgun is not configured. Set it with 'dotnet user-secrets set \"Mailgun:ApiKey\" <key>', " +
                "'dotnet user-secrets set \"Mailgun:Domain\" <domain>' and " +
                "'dotnet user-secrets set \"Mailgun:FromAddress\" \"e600Shop <no-reply@domain>\"' " +
                "(or the Mailgun__ApiKey / Mailgun__Domain / Mailgun__FromAddress environment variables).");
        }

        var endpoint =
            $"{(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/')}" +
            $"/v3/{domain.Trim()}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{apiKey}")));

        // multipart/form-data is what the Mailgun Messages API documents for this call.
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(fromAddress), "from");
        content.Add(new StringContent(email.RecipientEmail), "to");
        content.Add(new StringContent($"Order confirmation {email.OrderNumber}"), "subject");
        content.Add(new StringContent(BuildTextBody(email)), "text");
        content.Add(new StringContent(BuildHtmlBody(email)), "html");
        request.Content = content;

        using var response = await httpClientFactory
            .CreateClient(HttpClientName)
            .SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogError(
                "Mailgun rejected the confirmation for order {OrderNumber}: HTTP {Status} {Body}",
                email.OrderNumber,
                (int)response.StatusCode,
                body);

            throw new InvalidOperationException(
                $"Mailgun returned HTTP {(int)response.StatusCode} while sending the confirmation for order " +
                $"{email.OrderNumber}. Check Mailgun:ApiKey, Mailgun:Domain and that the sending domain is verified.");
        }

        logger.LogInformation(
            "Order confirmation {OrderNumber} queued with Mailgun for {Recipient}.",
            email.OrderNumber,
            email.RecipientEmail);
    }

    /// <summary>Currency formatted the same way the Angular app renders it (e.g. ₦1,234.56).</summary>
    private static string Money(decimal value) =>
        $"\u20A6{value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";

    private static string BuildTextBody(OrderConfirmationEmail email)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Hi {email.RecipientFirstName},");
        builder.AppendLine();
        builder.AppendLine(
            $"Thanks for your order {email.OrderNumber}. We have received it and will start preparing it right away.");
        builder.AppendLine();
        builder.AppendLine("Items");
        builder.AppendLine("-----");

        foreach (var line in email.Lines)
        {
            builder.AppendLine(
                $"  {line.Quantity} x {line.ProductName}  {Money(line.Subtotal)}  ({Money(line.UnitPrice)} each)");
        }

        builder.AppendLine();
        builder.AppendLine($"Subtotal  {Money(email.Subtotal)}");
        builder.AppendLine($"Shipping  {(email.Shipping == 0 ? "Free" : Money(email.Shipping))}");
        builder.AppendLine($"Total     {Money(email.TotalAmount)}");
        builder.AppendLine();
        builder.AppendLine($"Placed on {email.PlacedAtUtc.ToLongDateString()}.");
        builder.AppendLine();
        builder.AppendLine("Need to change something? Just reply to this e-mail.");

        return builder.ToString();
    }

    private static string BuildHtmlBody(OrderConfirmationEmail email)
    {
        var rows = new StringBuilder();

        foreach (var line in email.Lines)
        {
            rows.Append("<tr>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;\">")
                .Append(System.Net.WebUtility.HtmlEncode(line.ProductName))
                .Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;text-align:center;color:#64748b;\">")
                .Append(line.Quantity)
                .Append("</td>")
                .Append("<td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;text-align:right;\">")
                .Append(Money(line.Subtotal))
                .Append("</td>")
                .Append("</tr>");
        }

        var shippingRow = email.Shipping == 0 ? "Free" : Money(email.Shipping);
        var firstName = System.Net.WebUtility.HtmlEncode(email.RecipientFirstName);
        var orderNumber = System.Net.WebUtility.HtmlEncode(email.OrderNumber);

        var builder = new StringBuilder();
        builder.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f8fafc;");
        builder.Append("font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#0f172a;\">");
        builder.Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;");
        builder.Append("padding:32px;border:1px solid #e2e8f0;\">");
        builder.Append($"<h1 style=\"margin:0 0 4px;font-size:20px;\">Thanks, {firstName}!</h1>");
        builder.Append("<p style=\"margin:0 0 20px;color:#475569;\">Your order is confirmed and we are on it.</p>");
        builder.Append($"<p style=\"margin:0 0 16px;\"><strong>Order {orderNumber}</strong></p>");
        builder.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px;\">");
        builder.Append("<tr style=\"color:#64748b;\">");
        builder.Append("<th style=\"padding:8px 0;text-align:left;border-bottom:2px solid #e2e8f0;\">Item</th>");
        builder.Append("<th style=\"padding:8px 0;text-align:center;border-bottom:2px solid #e2e8f0;\">Qty</th>");
        builder.Append("<th style=\"padding:8px 0;text-align:right;border-bottom:2px solid #e2e8f0;\">Total</th>");
        builder.Append("</tr>");
        builder.Append(rows);
        builder.Append("</table>");
        builder.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px;margin-top:12px;\">");
        builder.Append("<tr><td style=\"padding:4px 0;color:#475569;\">Subtotal</td>");
        builder.Append($"<td style=\"padding:4px 0;text-align:right;\">{Money(email.Subtotal)}</td></tr>");
        builder.Append("<tr><td style=\"padding:4px 0;color:#475569;\">Shipping</td>");
        builder.Append($"<td style=\"padding:4px 0;text-align:right;\">{shippingRow}</td></tr>");
        builder.Append("<tr><td style=\"padding:10px 0;border-top:2px solid #e2e8f0;font-weight:700;\">Total</td>");
        builder.Append("<td style=\"padding:10px 0;border-top:2px solid #e2e8f0;text-align:right;font-weight:700;\">");
        builder.Append($"{Money(email.TotalAmount)}</td></tr>");
        builder.Append("</table>");
        builder.Append("</div></body></html>");

        return builder.ToString();
    }
}