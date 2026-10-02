using System.Net;
using System.Text;
using e600ShopApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace e600ShopApi.Tests;

/// <summary>
/// Exercises the Mailgun request the service actually builds — endpoint, Basic auth
/// and form payload — against a recording HttpMessageHandler. No socket is opened and
/// no real credentials are needed.
/// </summary>
public class MailgunEmailServiceTests
{
    private const string ApiKey = "test-private-key";
    private const string Domain = "sandbox-123.mailgun.org";
    private const string FromAddress = "e600Shop <no-reply@sandbox-123.mailgun.org>";
    private const string Recipient = "ada@example.com";

    // ------------------------------------------------------------------ fakes

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        /// <summary>
        /// Flattened multipart/form-data captured during the call — the request itself
        /// is disposed when the service returns, so the payload must be copied out now.
        /// </summary>
        public Dictionary<string, string> Form { get; } = new();

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name is not null)
                    {
                        Form[name] = await part.ReadAsStringAsync(cancellationToken);
                    }
                }
            }
            else if (request.Content is not null)
            {
                await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    "{\"id\":\"<fake@msg>\",\"message\":\"Queued. Thank you.\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    // --------------------------------------------------------------- helpers

    private static OrderConfirmationEmail BuildEmail() =>
        new(
            Recipient,
            "Ada",
            "E6-ABC12345",
            [new OrderEmailLine("Wireless Headphones", 2, 19.99m, 39.98m)],
            Subtotal: 39.98m,
            Shipping: 6.95m,
            TotalAmount: 46.93m,
            PlacedAtUtc: new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc));

    private static MailgunEmailService BuildService(
        RecordingHandler handler,
        Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Mailgun:ApiKey"] = ApiKey,
            ["Mailgun:Domain"] = Domain,
            ["Mailgun:FromAddress"] = FromAddress,
            ["Mailgun:BaseUrl"] = "https://api.mailgun.net",
        };

        foreach (var pair in overrides ?? new Dictionary<string, string?>())
        {
            values[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new MailgunEmailService(
            new StubHttpClientFactory(handler),
            configuration,
            NullLogger<MailgunEmailService>.Instance);
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task Send_PostsToTheMailgunMessagesEndpoint()
    {
        var handler = new RecordingHandler();

        await BuildService(handler).SendOrderConfirmationAsync(BuildEmail());

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal(
            $"https://api.mailgun.net/v3/{Domain}/messages",
            handler.Request.RequestUri?.ToString());
    }

    [Fact]
    public async Task Send_HonoursTheEuRegionalBaseUrl()
    {
        var handler = new RecordingHandler();

        await BuildService(handler, new()
        {
            ["Mailgun:BaseUrl"] = "https://api.eu.mailgun.net",
        }).SendOrderConfirmationAsync(BuildEmail());

        Assert.NotNull(handler.Request);
        Assert.StartsWith(
            "https://api.eu.mailgun.net/v3/",
            handler.Request.RequestUri?.ToString());
    }

    [Fact]
    public async Task Send_AuthenticatesWithBasicAuthUsingTheApiUsernameAndKey()
    {
        var handler = new RecordingHandler();

        await BuildService(handler).SendOrderConfirmationAsync(BuildEmail());

        Assert.NotNull(handler.Request?.Headers.Authorization);
        Assert.Equal("Basic", handler.Request.Headers.Authorization.Scheme);

        var decoded = Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.Request.Headers.Authorization.Parameter ?? string.Empty));

        Assert.Equal($"api:{ApiKey}", decoded);
    }

    [Fact]
    public async Task Send_SendsFromToSubjectAndBothBodies()
    {
        var handler = new RecordingHandler();

        await BuildService(handler).SendOrderConfirmationAsync(BuildEmail());

        Assert.NotNull(handler.Request);
        var form = handler.Form;

        Assert.Equal(FromAddress, form["from"]);
        Assert.Equal(Recipient, form["to"]);
        Assert.Equal("Order confirmation E6-ABC12345", form["subject"]);
        Assert.Contains("E6-ABC12345", form["text"]);
        Assert.Contains("Wireless Headphones", form["text"]);
        Assert.Contains("\u20A646.93", form["text"]);
        Assert.Contains("<table", form["html"]);
        Assert.Contains("Wireless Headphones", form["html"]);
    }

    [Fact]
    public async Task Send_WhenUnconfigured_ThrowsWithTheUserSecretsHint()
    {
        var handler = new RecordingHandler();
        var service = BuildService(handler, new() { ["Mailgun:ApiKey"] = "" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendOrderConfirmationAsync(BuildEmail()));

        Assert.Contains("Mailgun is not configured", exception.Message);
        Assert.Contains("dotnet user-secrets", exception.Message);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Send_WhenMailgunRejectsTheMessage_Throws()
    {
        var handler = new RecordingHandler { StatusCode = HttpStatusCode.Unauthorized };
        var service = BuildService(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendOrderConfirmationAsync(BuildEmail()));

        Assert.Contains("HTTP 401", exception.Message);
    }
}