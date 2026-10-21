using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Akanti.API.Data;
using Akanti.API.Interfaces;
using Akanti.API.Models;
using Akanti.API.Services;
using Akanti.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Akanti API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configuration already merges appsettings.json with the environment, so
// ConnectionStrings__DefaultConnection overrides the committed default.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionFromEnv = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));

// Log the resolved host (never the password) so a misconfigured deploy is
// obvious in the logs instead of failing later with a connection error.
var dbSummary = "not set";
if (!string.IsNullOrWhiteSpace(connectionString))
{
    try
    {
        var kv = connectionString.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
        var host = kv.TryGetValue("host", out var h) ? h : "?";
        var db = kv.TryGetValue("database", out var d) ? d : "?";
        var port = kv.TryGetValue("port", out var pt) ? pt : "?";
        dbSummary = $"{host}:{port}/{db}";
    }
    catch
    {
        dbSummary = "(unparseable connection string)";
    }
}
Console.WriteLine($"Using database: {dbSummary} (source: {(connectionFromEnv ? "environment" : "appsettings.json")})");

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o =>
    {
        o.CommandTimeout(120);
        o.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
    }), poolSize: 100);

// Ensure configuration keys match your appsettings.json structure
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];

// Secrets (JWT signing key, SMTP credentials, admin password) are supplied via
// environment variables, never committed. Fail fast rather than start with an empty
// key: a blank signing key would make every token trivially forgeable, and an
// unnoticed misconfiguration here is worse than a startup failure.
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set the JWT__KEY environment variable " +
        "(or Jwt:Key in a local, untracked settings file) before starting the API.");
}

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters (256 bits) for HS256 signing.");
}

var validateIssuer = !string.IsNullOrEmpty(jwtIssuer);
var validateAudience = !string.IsNullOrEmpty(jwtAudience);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = validateIssuer,
            ValidateAudience = validateAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),
            ClockSkew = TimeSpan.Zero
        };

        // 2. Logs exact validation failures directly to your terminal
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"\n❌ [JWT Auth Failed]: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("\n✅ [JWT Authenticated Successfully]");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        var origins = new List<string> { "http://localhost:3000", "http://localhost:5173", "http://localhost:5174", "http://localhost:5180" };

        // FRONTEND_URL may hold several origins separated by commas or semicolons, so a
        // project with more than one Vercel domain (or a rename mid-flight) does not
        // silently lose access.
        var configured = Environment.GetEnvironmentVariable("FRONTEND_URL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            origins.AddRange(configured
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(o => o.TrimEnd('/'))
                .Where(o => o.Length > 0));
        }

        policy.WithOrigins(origins.Distinct().ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ---------------------------------------------------------------------------
// Rate limiting
// Auth endpoints are partitioned by client IP. Limits are deliberately strict
// on the expensive / abuse-prone endpoints (registration, code sending, login)
// and generous on cheap informational ones. Rejections return 429.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Too many requests. Please wait a moment and try again.\"}",
            cancellationToken);
    };

    static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // NOTE: partition keys come from the *resolved* client IP. Forwarded headers
    // (configured below) must be trusted only for known proxies, otherwise a
    // caller could spoof X-Forwarded-For and dodge these limits entirely.

    // Sliding windows give smoother behaviour than fixed windows: a burst that
    // straddles the boundary cannot sneak through double the limit.
    static RateLimitPartition<string> Partition(HttpContext http, int permitLimit, TimeSpan window, int segments) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            ClientKey(http),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                SegmentsPerWindow = segments,
                QueueLimit = 0
            });

    // Register: 5 accounts per IP per 10 minutes.
    options.AddPolicy("auth-register", http => Partition(http, permitLimit: 5, TimeSpan.FromMinutes(10), segments: 5));

    // Login / change-password: 10 attempts per IP per 5 minutes.
    options.AddPolicy("auth-login", http => Partition(http, permitLimit: 10, TimeSpan.FromMinutes(5), segments: 5));

    // Sending verification / reset codes: 5 per IP per 10 minutes.
    options.AddPolicy("auth-codes", http => Partition(http, permitLimit: 5, TimeSpan.FromMinutes(10), segments: 5));

    // Code checking (verify-email / verify-reset-code): 15 per IP per 5 minutes,
    // which still allows honest retries after a typo but stops brute-forcing.
    options.AddPolicy("auth-verify", http => Partition(http, permitLimit: 15, TimeSpan.FromMinutes(5), segments: 5));
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IIncomeService, IncomeService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<IDebtService, DebtService>();
builder.Services.AddScoped<ICashFlowService, CashFlowService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<BackgroundEmailQueue>();
builder.Services.AddSingleton<IBackgroundEmailQueue>(sp => sp.GetRequiredService<BackgroundEmailQueue>());
builder.Services.AddHostedService<BackgroundEmailService>();

// Email goes out over HTTP rather than SMTP: Render blocks outbound SMTP, so a Gmail
// connection from the deployed app times out. A named client gives one pooled,
// reusable handler for every send.
builder.Services.AddHttpClient("EmailApi", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<AuditLogQueue>();
builder.Services.AddSingleton<IAuditLogQueue>(sp => sp.GetRequiredService<AuditLogQueue>());
builder.Services.AddHostedService<AuditLogProcessorService>();
builder.Services.AddHostedService<DebtReminderService>();

// ---------------------------------------------------------------------------
// Forwarded headers
// Behind a reverse proxy (Render, Fly, nginx, ...) the real client IP arrives in
// X-Forwarded-For. Rate limiting partitions on the client IP, so without this
// every user behind the proxy would share a single bucket.
//
// Options are built at the middleware call (see UseForwardedHeaders below) rather
// than via DI Configure: the framework's default configuration re-adds loopback
// trust, which would let a local caller forge X-Forwarded-For and rotate the key.
// ---------------------------------------------------------------------------

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------------------------------------------------------------------------
// Forwarded headers (applied conditionally — see security note below)
//
// ASP.NET Core's ForwardedHeadersMiddleware always trusts a LOOPBACK peer,
// regardless of KnownProxies/KnownNetworks. On this app that matters: a local
// caller could send X-Forwarded-For and rotate the rate-limit partition key,
// bypassing the auth limits entirely. Clearing the known lists does NOT disable it.
//
// So the middleware is registered only when proxying is actually configured, and
// then only for the listed proxies/CIDRs (or all, for a PaaS that rewrites the
// header). In the default local setup it is not registered at all, which is the
// safe behaviour: the peer address is used verbatim.
// ---------------------------------------------------------------------------
var trustAllProxies = builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies");
var knownProxies = (builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    .Select(s => IPAddress.TryParse(s, out var ip) ? ip : null)
    .Where(ip => ip is not null)
    .Select(ip => ip!)
    .ToList();
var knownNetworks = (builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
    .Select(ParseCidr)
    .Where(n => n is not null)
    .Select(n => n!)
    .ToList();

if (trustAllProxies || knownProxies.Count > 0 || knownNetworks.Count > 0)
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        // Trust only the closest hop; prevents a caller from prepending extra entries.
        ForwardLimit = 1
    };

    if (!trustAllProxies)
    {
        // KnownProxies/KnownNetworks are empty by default in ForwardedHeadersOptions,
        // so nothing beyond the implicit loopback case is trusted unless added here.
        foreach (var ip in knownProxies) forwardedOptions.KnownProxies.Add(ip);
        foreach (var net in knownNetworks) forwardedOptions.KnownNetworks.Add(net);
    }

    app.UseForwardedHeaders(forwardedOptions);
    Console.WriteLine($"Forwarded headers enabled (trustAllProxies={trustAllProxies}, proxies={knownProxies.Count}, networks={knownNetworks.Count}).");
}
else
{
    Console.WriteLine("Forwarded headers disabled (no trusted proxies configured). Client IP is taken from the connection.");
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditMiddleware>();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization skipped (tables likely exist): {ex.Message}");
    }

    SeedAdminUser(dbContext, builder.Configuration);
}

// Mail configuration is easy to get subtly wrong (Email__Provider vs Email:Provider,
// a missing key, a From address that differs from the one the provider verified), and
// the symptom is identical in every case: no email arrives. Print the resolved state at
// boot so the cause is visible without waiting for a failed send. Never print the key.
{
    var emailProvider = (builder.Configuration["Email:Provider"] ?? "").Trim();
    var emailFrom = builder.Configuration["Email:FromEmail"]
        ?? builder.Configuration["Smtp:FromEmail"]
        ?? "";
    var hasApiKey = !string.IsNullOrWhiteSpace(builder.Configuration["Email:ApiKey"]);

    Console.WriteLine(
        $"Email configuration: provider={(emailProvider == "" ? "smtp (Email:Provider not set)" : emailProvider)}, " +
        $"from={(emailFrom == "" ? "(not set)" : emailFrom)}, " +
        $"apiKey={(hasApiKey ? "set" : "not set")}");

    if (emailProvider != "" && !hasApiKey)
    {
        Console.WriteLine(
            $"WARNING: Email:Provider is '{emailProvider}' but Email:ApiKey is empty. " +
            "Sends will fail. Set Email__ApiKey (double underscore) in the environment.");
    }
}

app.Run();

static void SeedAdminUser(ApplicationDbContext context, IConfiguration configuration)
{
    var adminEmail = configuration["AdminEmail"];
    if (string.IsNullOrEmpty(adminEmail))
        return;

    // Emails are stored lowercased, so the seeded admin has to be stored the same way.
    // A mixed-case value here would otherwise seed a second admin row that
    // duplicates the existing one.
    adminEmail = adminEmail.Trim().ToLowerInvariant();

    if (context.Users.AsNoTracking().Any(u => u.Email == adminEmail))
        return;

    // No built-in default: an unset password must NOT silently create an admin
    // account with a well-known credential. Skip seeding and say so instead.
    var adminPassword = configuration["AdminPassword"];
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        Console.WriteLine(
            $"Admin seeding skipped: no ADMINPASSWORD configured for {adminEmail}. " +
            "Set the ADMINPASSWORD environment variable to create the initial admin.");
        return;
    }

    context.Users.Add(new User
    {
        FullName = "Akanti Admin",
        Email = adminEmail,
        PasswordHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(adminPassword))),
        UserType = UserType.PersonalFinance,
        IsActive = true,
        IsAdmin = true,
        IsEmailVerified = true,
        CreatedAt = DateTime.UtcNow
    });

    context.SaveChanges();
    Console.WriteLine($"Seeded admin user: {adminEmail}");
}

// Parses "192.168.1.0/24" into an IPNetwork, or null when malformed.
static Microsoft.AspNetCore.HttpOverrides.IPNetwork? ParseCidr(string value)
{
    var parts = value.Split('/', 2);
    if (parts.Length != 2) return null;
    if (!IPAddress.TryParse(parts[0], out var prefix)) return null;
    if (!int.TryParse(parts[1], out var length)) return null;

    try
    {
        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length);
    }
    catch (ArgumentOutOfRangeException)
    {
        Console.WriteLine($"Ignoring invalid CIDR in ForwardedHeaders config: {value}");
        return null;
    }
}