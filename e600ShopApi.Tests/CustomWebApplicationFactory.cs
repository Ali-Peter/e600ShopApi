using e600ShopApi.Data;
using e600ShopApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace e600ShopApi.Tests;

/// <summary>
/// Test double for <see cref="IEmailService"/>: records every confirmation instead of
/// calling Mailgun, so no test opens a socket or needs credentials. Tests read
/// <see cref="Sent"/> (filtering by order number, since the factory is shared) to
/// assert exactly what would have been mailed.
/// </summary>
public sealed class RecordingEmailService : IEmailService
{
    private readonly List<OrderConfirmationEmail> sent = [];

    public IReadOnlyList<OrderConfirmationEmail> Sent
    {
        get
        {
            lock (sent)
            {
                return sent.ToList();
            }
        }
    }

    public Task SendOrderConfirmationAsync(
        OrderConfirmationEmail email,
        CancellationToken cancellationToken = default)
    {
        lock (sent)
        {
            sent.Add(email);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Boots the real API pipeline for integration tests. PostgreSQL is replaced with an
/// isolated EF Core in-memory database (one per factory instance), and the Supabase
/// connection string is overridden with a never-used placeholder so tests run without
/// machine-specific secrets and never open a connection to the real database.
/// Mailgun is likewise replaced with <see cref="RecordingEmailService"/> so the
/// confirmation path is exercised without ever sending mail.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"e600shop-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Applied after the app's own configuration sources, so this value wins.
        // It is only read by Program.cs at startup; nothing ever connects with it
        // because the DbContext options are replaced below.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Supabase"] =
                    "Host=unused;Port=5432;Database=unused;Username=unused;Password=unused",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the registrations added by Program.cs so Npgsql is never used.
            // EF Core 10 registers IDbContextOptionsConfiguration in addition to the
            // options type and the context itself.
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            // Swap the real Mailgun sender for a recorder.
            services.RemoveAll(typeof(IEmailService));
            services.AddSingleton<RecordingEmailService>();
            services.AddSingleton<IEmailService>(
                provider => provider.GetRequiredService<RecordingEmailService>());
        });
    }
}
