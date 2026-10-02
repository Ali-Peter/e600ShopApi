using System.Text;
using e600ShopApi.Data;
using e600ShopApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// MVC controllers + API explorer
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ---------------------------------------------------------------------------
// Swagger / OpenAPI with a "Bearer" JWT authorisation button
// ---------------------------------------------------------------------------
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "e600Shop API",
            Version = "v1",
            Description = "REST API for the e600Shop e-commerce application.",
        });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste a JWT access token (without the 'Bearer ' prefix).",
        });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

// ---------------------------------------------------------------------------
// Entity Framework Core + PostgreSQL (Supabase)
// The connection string MUST come from configuration (user-secrets or the
// ConnectionStrings__Supabase environment variable) — never from source code.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Supabase");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        connectionString
        ?? throw new InvalidOperationException(
            "Connection string 'Supabase' is not configured. " +
            "Set it with 'dotnet user-secrets set \"ConnectionStrings:Supabase\" \"<value>\"' " +
            "or the ConnectionStrings__Supabase environment variable.")));

// ---------------------------------------------------------------------------
// Transactional e-mail (Mailgun)
// The private API key lives in user-secrets / Mailgun__* environment variables and
// is read only on the server — the Angular client has no path to these values.
// Delivery is best-effort: OrdersController logs and continues when Mailgun is down
// or unconfigured, so an order is never lost to a failed e-mail.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient(MailgunEmailService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IEmailService, MailgunEmailService>();

// ---------------------------------------------------------------------------
// JWT authentication architecture (tokens are issued by ITokenService; Google
// sign-in is handled by the Angular client through Supabase)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ITokenService, TokenService>();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "e600ShopApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "e600Shop";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            // The signing key is resolved from configuration when a token is validated,
            // so no secrets are ever stored in source code. When Jwt:Secret is missing
            // no key is supplied and every token fails validation (fail closed).
            IssuerSigningKeyResolver = (_, _, _, _) =>
            {
                var secret = builder.Configuration["Jwt:Secret"];
                if (string.IsNullOrWhiteSpace(secret))
                {
                    return Enumerable.Empty<SecurityKey>();
                }

                return new SecurityKey[]
                {
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                };
            },
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// CORS — allowed frontend origins come from configuration so production can
// point at the deployed Angular site without code changes:
//   Cors__AllowedOrigins__0=https://www.example.com   (extra origins: __1, __2, …)
// appsettings.json keeps http://localhost:4200 as the development default.
// Blank entries are ignored; when nothing valid is configured the localhost
// development origin is used.
// ---------------------------------------------------------------------------
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];

var allowedOrigins = configuredOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .ToArray();

if (allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://localhost:4200"];
}

builder.Services.AddCors(options =>
    options.AddPolicy(
        "Frontend",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "e600Shop API v1");
    });
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Entry point marker so WebApplicationFactory<Program> can boot the app in tests.
// Compile-time only; no runtime behaviour changes.
public partial class Program
{
}
