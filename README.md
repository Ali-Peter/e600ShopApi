# e600ShopApi

ASP.NET Core Web API backend for the **e600Shop** e-commerce frontend (`../e600Shop`).

## Stack

- ASP.NET Core Web API (controllers) on .NET 10
- Entity Framework Core 10 + PostgreSQL (Supabase)
- JWT bearer token architecture (`ITokenService` / `TokenService`)
- Google sign-in handled by the Angular client through Supabase Auth (the API itself has no login endpoint)
- Order confirmation e-mail via **Mailgun** (best-effort: a failed e-mail never loses an order)
- Swagger/OpenAPI (Swashbuckle) at `/swagger` in Development
- xUnit test project

## Project layout

```
e600ShopApi.slnx
├── e600ShopApi/               API project
│   ├── Controllers/           ProductsController, OrdersController, HealthController
│   ├── Data/                  AppDbContext + design-time factory
│   ├── Domain/                Product, User, Order, OrderItem, Address, OrderStatus
│   ├── Services/              TokenService (JWT), MailgunEmailService (order e-mail)
│   ├── Migrations/            EF Core migrations (InitialCreate)
│   └── Program.cs             Service wiring (EF, JWT, Swagger, CORS, Mailgun)
└── e600ShopApi.Tests/         Unit tests (domain, EF model, tokens, products, orders, Mailgun)
```

## Domain model & relationships

- `User` 1—* `Order` (delete **restricted** so order history is never lost)
- `Order` 1—* `OrderItem` (delete **cascades**)
- `OrderItem` *—1 `Product` (delete **restricted** so order lines are never orphaned)
- `User` 1—* `Address` (delete **cascades**)
- All money columns are `numeric(18,2)`; `Order.Status` is stored as `varchar(32)`;
  `User.Email` and `User.GoogleSubjectId` are unique indexes; `OrderItem.Quantity > 0` is a check constraint.

## Configuration (no secrets in source code)

Secrets are read from configuration. Set them per machine with **user-secrets**
(development) or with **environment variables** (staging/production):

```bash
cd e600ShopApi
dotnet user-secrets init   # already done — writes a UserSecretsId into the csproj
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=aws-0.<region>.pooler.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<db-password>"
dotnet user-secrets set "Jwt:Secret" "<random string of at least 32 characters>"
dotnet user-secrets set "Mailgun:ApiKey" "<your-mailgun-api-key>"
dotnet user-secrets set "Mailgun:Domain" "<your-mailgun-domain>"
dotnet user-secrets set "Mailgun:FromAddress" "orders@<your-mailgun-domain>"
```

Environment variable equivalents use `__`: `ConnectionStrings__Supabase`,
`Jwt__Secret`, `Mailgun__ApiKey`, `Mailgun__Domain`, `Mailgun__FromAddress`,
`Mailgun__BaseUrl`, `Cors__AllowedOrigins__0`.

> `appsettings.json` contains only empty placeholders plus non-secret defaults
> (`Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpirationMinutes`, `Cors:AllowedOrigins`,
> `Mailgun:BaseUrl`). Development values live in the machine-local user-secrets
> store (`secrets.json`), which is excluded from source control.

## Production configuration

Supply these as **environment variables** on the hosting environment (Docker,
systemd, PaaS dashboard, …). Placeholders only — never commit real values.

| Variable | Purpose | Example (placeholders only) |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | .NET environment name | `Production` |
| `ConnectionStrings__Supabase` | Supabase PostgreSQL connection string | `Host=aws-0.<region>.pooler.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<db-password>` |
| `Jwt__Secret` | JWT signing key (≥ 32 random characters) | `<random-string-at-least-32-characters>` |
| `Mailgun__ApiKey` | Mailgun private API key (server-side only) | `key-<your-mailgun-api-key>` |
| `Mailgun__Domain` | Sending domain | `mg.example.com` or `<project>.mailgun.org` (sandbox) |
| `Mailgun__FromAddress` | From address for order confirmations | `orders@mg.example.com` |
| `Mailgun__BaseUrl` | Mailgun API region | `https://api.mailgun.net` (default) or `https://api.eu.mailgun.net` (EU region) |
| `Cors__AllowedOrigins__0` | Deployed frontend origin | `https://www.example.com` (extra origins: `__1`, `__2`, …) |

Notes:

- `Cors:AllowedOrigins` defaults to `http://localhost:4200` in `appsettings.json`
  (development). The environment variable **overrides index 0**, so list localhost
  explicitly as well when the API must serve both: `Cors__AllowedOrigins__0=http://localhost:4200`,
  `Cors__AllowedOrigins__1=https://www.example.com`.
- Mailgun delivery is **best-effort**: `POST /api/Orders` still returns 201 and
  persists the order if Mailgun is unreachable; the response reports
  `emailSent: false` and the failure is logged server-side.
- On a Mailgun **sandbox** domain, every recipient must be authorized in the
  Mailgun dashboard before it can receive mail.

### Supabase database configuration

1. Supabase Dashboard → your project → **Connect** → *Session pooler / ORM* →
   copy the PostgreSQL connection string.
2. Expose it as `ConnectionStrings__Supabase` (or user-secrets for development).
3. Apply EF migrations after each deploy:

   ```bash
   dotnet ef database update --project e600ShopApi --startup-project e600ShopApi
   ```

4. Keep the database password and the Supabase `service_role` key strictly
   server-side — the frontend only ever holds the public project URL and the
   publishable (anon) key.

### Google Cloud OAuth configuration

Google sign-in in the Angular client is already implemented and tested; only the
external console configuration is deployment-specific:

1. Google Cloud Console → **APIs & Services → OAuth consent screen** (External;
   add your domain as needed).
2. **Credentials → Create credentials → OAuth client ID → Web application**:
   - Authorized JavaScript origins: `http://localhost:4200` (local development),
     `https://<production-frontend>` (production).
   - Authorized redirect URI (**Supabase OAuth callback** — Google redirects
     here, not to your site directly):
     `https://<project-ref>.supabase.co/auth/v1/callback`
     where `<project-ref>` matches `https://<project-ref>.supabase.co` in
     `e600Shop/src/app/core/config/supabase.config.ts`.

### Supabase Google provider + redirect URLs

1. Supabase Dashboard → **Authentication → Providers → Google**: paste the Google
   Cloud OAuth **Client ID** and **Client secret**.
2. Supabase Dashboard → **Authentication → URL configuration**:
   - **Site URL**: `https://<production-frontend>` (production) or
     `http://localhost:4200` (local).
   - **Redirect URLs** must include both:
     - Local development redirect: `http://localhost:4200/login`
     - Production frontend redirect: `https://<production-frontend>/login`

The client already signs in with `redirectTo: <origin>/login`, so no code change
is required — just these dashboard settings.

## Database (Supabase PostgreSQL)

1. Create a Supabase project and copy the connection string
   (Dashboard → Connect → ORM / Session pooler → connection string).
2. Store it as shown above.
3. Apply migrations:

```bash
dotnet ef database update --project e600ShopApi --startup-project e600ShopApi
```

Create new migrations with:

```bash
dotnet ef migrations add <Name> --project e600ShopApi --startup-project e600ShopApi
```

> Migrations can be generated without a database — `DesignTimeDbContextFactory`
> supplies a placeholder connection string that is never used to connect.

## Build, test, run

```bash
dotnet restore e600ShopApi.slnx
dotnet build e600ShopApi.slnx -c Release
dotnet test  e600ShopApi.slnx -c Release
dotnet run --project e600ShopApi
```

- Swagger UI: `http://localhost:<port>/swagger` (Development only, includes an Authorize button for bearer tokens)
- Health probe: `GET /api/health`
- Products: `GET /api/products`, `GET /api/products?category=electronics`, `GET /api/products?search=yoga`, `GET /api/products/{id}`

## CORS

The `Frontend` policy allows only origins from configuration:

- Development default (`appsettings.json`): `http://localhost:4200`
- Production: set `Cors__AllowedOrigins__0=https://<production-frontend>`
  (additional origins via `__1`, `__2`, …). Blank or missing entries fall back
  to the localhost development default.

## Known limitations (by design)

- The API has **no login endpoint**: Google sign-in runs entirely in the Angular
  client through Supabase Auth (`User.GoogleSubjectId` records the Google subject).
- Orders are guest checkout — no endpoint currently requires a signed-in user.
  JWT bearer middleware is wired up (`ITokenService` can issue tokens) but
  unused until API-side authorization lands.
