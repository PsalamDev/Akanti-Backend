using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Allow override via environment variable (standard convention, e.g. Azure App Service / GitHub Actions)
var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (!string.IsNullOrEmpty(envConnectionString)) connectionString = envConnectionString;

Console.WriteLine($"Using database: {connectionString?.Substring(0, Math.Min(connectionString?.Length ?? 0, 50))}...");

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, o =>
    {
        o.CommandTimeout(120);
        o.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
    }), poolSize: 100);

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
        var origins = new List<string> { "http://localhost:3000", "http://localhost:5173", "http://localhost:5174", "http://localhost:5180" };
        var vercelUrl = Environment.GetEnvironmentVariable("FRONTEND_URL");
        if (!string.IsNullOrEmpty(vercelUrl)) origins.Add(vercelUrl);

        policy.WithOrigins(origins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
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
builder.Services.AddSingleton<BackgroundEmailQueue>();
builder.Services.AddSingleton<IBackgroundEmailQueue>(sp => sp.GetRequiredService<BackgroundEmailQueue>());
builder.Services.AddHostedService<BackgroundEmailService>();
builder.Services.AddSingleton<AuditLogQueue>();
builder.Services.AddSingleton<IAuditLogQueue>(sp => sp.GetRequiredService<AuditLogQueue>());
builder.Services.AddHostedService<AuditLogProcessorService>();
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

    SeedAdminUser(dbContext, builder.Configuration);
}

app.Run();

static void SeedAdminUser(ApplicationDbContext context, IConfiguration configuration)
{
    var adminEmail = configuration["AdminEmail"];
    if (string.IsNullOrEmpty(adminEmail))
        return;

    if (context.Users.AsNoTracking().Any(u => u.Email == adminEmail))
        return;

    var adminPassword = configuration["AdminPassword"] ?? "Admin@12345";

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