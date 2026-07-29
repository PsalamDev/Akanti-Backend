using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Akanti.API.Data;
using Akanti.API.Services;
using Akanti.API.Middleware;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Try DATABASE_URL env var (used by Render / Supabase)
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl) && !databaseUrl.Contains("[YOUR"))
{
    // Convert URI format (postgresql://user:pass@host:port/db) to key=value format
    if (databaseUrl.StartsWith("postgresql://") || databaseUrl.StartsWith("postgres://"))
    {
        try
        {
            // Manual parse to handle @ in password
            var withoutScheme = databaseUrl.Substring(databaseUrl.IndexOf("://") + 3);
            var atIndex = withoutScheme.LastIndexOf('@');
            var userInfo = withoutScheme.Substring(0, atIndex);
            var hostPart = withoutScheme.Substring(atIndex + 1);

            var colonIdx = userInfo.IndexOf(':');
            var username = colonIdx >= 0 ? Uri.UnescapeDataString(userInfo.Substring(0, colonIdx)) : Uri.UnescapeDataString(userInfo);
            var password = colonIdx >= 0 ? Uri.UnescapeDataString(userInfo.Substring(colonIdx + 1)) : "";

            // Split host:port/db
            var slashIdx = hostPart.IndexOf('/');
            var hostPort = slashIdx >= 0 ? hostPart.Substring(0, slashIdx) : hostPart;
            var db = slashIdx >= 0 ? hostPart.Substring(slashIdx + 1) : "";

            var portIdx = hostPort.LastIndexOf(':');
            var host = portIdx >= 0 ? hostPort.Substring(0, portIdx) : hostPort;
            var port = portIdx >= 0 ? hostPort.Substring(portIdx + 1) : "5432";

            connectionString = $"Host={host};Port={port};Database={db};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Pooling=false";
        }
        catch
        {
            Console.WriteLine("Failed to parse DATABASE_URL as URI, using raw value");
            connectionString = databaseUrl;
        }
    }
    else
    {
        connectionString = databaseUrl;
    }
}

Console.WriteLine($"Using database: {connectionString?.Substring(0, Math.Min(connectionString?.Length ?? 0, 50))}...");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o =>
    {
        o.CommandTimeout(120);
        o.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
    }));

// Ensure configuration keys match your appsettings.json structure
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];

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
        var origins = new List<string> { "http://localhost:3000", "http://localhost:5173" };
        var vercelUrl = Environment.GetEnvironmentVariable("FRONTEND_URL");
        if (!string.IsNullOrEmpty(vercelUrl)) origins.Add(vercelUrl);

        policy.WithOrigins(origins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddHttpClient<IAIService, AIService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHostedService<DebtReminderService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
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
}

app.Run();