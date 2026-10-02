using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace e600ShopApi.Data;

/// <summary>
/// Builds the DbContext for EF Core design-time commands (migrations add, database update, …)
/// using the same configuration sources as the running application:
/// appsettings.json → appsettings.{Environment}.json → user secrets → environment variables.
/// No connection string, password or host is stored in source code, and the value of
/// "ConnectionStrings:Supabase" is never written to any output.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        // Base path is the build output directory, where the Web SDK copies
        // appsettings.json — this works no matter which directory `dotnet ef` runs from.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Supabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Deliberately does not include any configuration values in the message.
            throw new InvalidOperationException(
                "Connection string 'Supabase' is not configured for design-time operations. "
                + "Set it with 'dotnet user-secrets set \"ConnectionStrings:Supabase\" \"<value>\"' "
                + "or the ConnectionStrings__Supabase environment variable.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
