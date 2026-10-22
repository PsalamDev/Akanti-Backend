using Npgsql;
using System;

namespace Akanti.API.Configuration;

/// <summary>
/// Resolves the PostgreSQL connection string for the API.
/// <para>
/// The committed <c>ConnectionStrings:DefaultConnection</c> is only a fallback. A local
/// Postgres rarely listens on the same port as the one that was committed, and when it
/// does not, every query fails with a connection error that looks like an application
/// fault rather than a configuration one. The discrete <c>Database:*</c> settings let a
/// developer override just the port (or host, or credentials) without having to
/// reconstruct the whole connection string.
/// </para>
/// <para>
/// Precedence, highest first:
/// <list type="number">
///   <item><description><c>ConnectionStrings:DefaultConnection</c> from the environment
///   (<c>ConnectionStrings__DefaultConnection</c>), used verbatim. A full override wins
///   outright so a deploy can pin an exact string.</description></item>
///   <item><description>Each <c>Database:Host</c>, <c>Database:Port</c>,
///   <c>Database:Name</c>, <c>Database:Username</c> and <c>Database:Password</c> value that
///   is set, applied over the committed connection string.</description></item>
///   <item><description>The committed connection string unchanged.</description></item>
/// </list>
/// A <c>Database:Port</c> on its own is enough to fix the common case, because the host,
/// database name and credentials stay as they are.
/// </para>
/// </summary>
public static class DatabaseConnectionString
{
    /// <summary>
    /// Builds the connection string and returns it together with a description of where
    /// each part came from, so startup can log the resolved state without the password.
    /// </summary>
    public static (string ConnectionString, string Source) Resolve(IConfiguration configuration)
    {
        // Prefer the environment variable directly. IConfiguration["ConnectionStrings:DefaultConnection"]
        // reads the same merged value as GetConnectionString in most setups, so the previous
        // "env override" branch was effectively dead code. Check the process environment first
        // so the logged source is accurate and deploys that inject ConnectionStrings__DefaultConnection
        // actually take precedence.
        var environmentOverride = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(environmentOverride))
        {
            environmentOverride = configuration["ConnectionStrings:DefaultConnection"];
        }

        var committed = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            // If it's identical to the committed/fallback string, it's not an intentional override;
            // fall through so discrete Database:* variables can still be applied.
            if (string.IsNullOrWhiteSpace(committed) || !string.Equals(environmentOverride, committed, StringComparison.Ordinal))
            {
                return (environmentOverride, "environment (ConnectionStrings__DefaultConnection)");
            }
        }

        var baseConnection = !string.IsNullOrWhiteSpace(committed)
            ? committed
            : "Host=localhost;Port=5432;Database=AkantiDb;Username=postgres;Password=postgres;";

        var builder = new NpgsqlConnectionStringBuilder(baseConnection);
        var applied = new List<string>();

        // Allow the environment to override discrete parts too (common in PaaS).
        ApplyIfSet(Environment.GetEnvironmentVariable("Database__Host") ?? configuration["Database:Host"], builder.Host ?? string.Empty, v => builder.Host = v, applied, "Host");
        ApplyIfSet(Environment.GetEnvironmentVariable("Database__Port") ?? configuration["Database:Port"], builder.Port.ToString(), v =>
        {
            if (int.TryParse(v, out var port))
            {
                builder.Port = port;
            }
        }, applied, "Port");
        ApplyIfSet(Environment.GetEnvironmentVariable("Database__Name") ?? configuration["Database:Name"], builder.Database ?? string.Empty, v => builder.Database = v, applied, "Name");
        ApplyIfSet(Environment.GetEnvironmentVariable("Database__Username") ?? configuration["Database:Username"], builder.Username ?? string.Empty, v => builder.Username = v, applied, "Username");
        ApplyIfSet(Environment.GetEnvironmentVariable("Database__Password") ?? configuration["Database:Password"], builder.Password ?? string.Empty, v => builder.Password = v, applied, "Password");

        var source = applied.Count == 0
            ? string.IsNullOrWhiteSpace(committed) ? "defaults" : "appsettings.json"
            : $"appsettings.json + Database:{string.Join(", Database:", applied)}";

        return (builder.ConnectionString, source);
    }

    private static void ApplyIfSet(
        string? configuredValue,
        string currentValue,
        Action<string> apply,
        List<string> applied,
        string label)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            return;

        var value = configuredValue.Trim();
        if (string.Equals(value, currentValue, StringComparison.Ordinal))
            return;

        apply(value);
        applied.Add(label);
    }
}
