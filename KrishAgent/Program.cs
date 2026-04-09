using KrishAgent.Configuration;
using KrishAgent.Services;
using Microsoft.EntityFrameworkCore;
using KrishAgent.Data;
using System.Reflection;
using System.Net;
using Microsoft.Extensions.Options;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
ConfigureHostingPort(builder);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<AlpacaOptions>(builder.Configuration.GetSection(AlpacaOptions.SectionName));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.Configure<TradingOptions>(builder.Configuration.GetSection(TradingOptions.SectionName));

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddSingleton<NodeBridgeService>();
builder.Services.AddHttpClient<MarketService>()
    .ConfigurePrimaryHttpMessageHandler(CreateExternalApiHandler);
builder.Services.AddScoped<IndicatorService>();
builder.Services.AddHttpClient<AIService>()
    .ConfigurePrimaryHttpMessageHandler(CreateExternalApiHandler);
builder.Services.AddSingleton<LiveMarketStreamService>();
builder.Services.AddScoped<DataService>();
builder.Services.AddScoped<IntradayTradingService>();
builder.Services.AddScoped<PennyStockTradingService>();
builder.Services.AddHostedService<TradingDataService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<LiveMarketStreamService>());
builder.Services.AddDbContext<KrishAgent.Data.TradingContext>(options =>
{
    var databaseConfig = ResolveDatabaseConfiguration(builder.Configuration);
    if (databaseConfig.Provider == "postgres")
    {
        options.UseNpgsql(databaseConfig.ConnectionString, postgres => postgres.EnableRetryOnFailure());
    }
    else
    {
        options.UseSqlite(databaseConfig.ConnectionString);
    }
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        var allowedOrigins = ResolveAllowedOrigins(builder.Configuration);
        policy.WithOrigins(allowedOrigins)
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

await using (var startupScope = app.Services.CreateAsyncScope())
{
    var dbContext = startupScope.ServiceProvider.GetRequiredService<TradingContext>();
    var dataService = startupScope.ServiceProvider.GetRequiredService<DataService>();
    var tradingOptions = startupScope.ServiceProvider.GetRequiredService<IOptions<TradingOptions>>();
    var startupLogger = startupScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    try
    {
        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.EnsureCreatedAsync();
            startupLogger.LogInformation("Database schema ensured successfully for PostgreSQL");
        }
        else
        {
            await dbContext.Database.MigrateAsync();
            startupLogger.LogInformation("Database migrations applied successfully");
        }

        await dataService.EnsureWatchlistsSeededAsync(tradingOptions.Value);
        startupLogger.LogInformation("Database seed completed successfully");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowLocalhost");

app.MapControllers();

app.Run();

static HttpMessageHandler CreateExternalApiHandler()
{
    var handler = new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    };

    // Codex sessions set loopback port 9 as a blackhole proxy. Ignore it so local dev can still reach external APIs.
    if (ShouldBypassProxy())
    {
        handler.UseProxy = false;
    }

    return handler;
}

static void ConfigureHostingPort(WebApplicationBuilder builder)
{
    var port = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort))
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
    }
}

static string[] ResolveAllowedOrigins(IConfiguration configuration)
{
    var defaults = new[]
    {
        "http://localhost:3000",
        "http://localhost:3001",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:3001"
    };

    var configuredOrigins = configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
    var envOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return defaults
        .Concat(configuredOrigins)
        .Concat(envOrigins)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static DatabaseConfiguration ResolveDatabaseConfiguration(IConfiguration configuration)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        return new DatabaseConfiguration("postgres", ConvertDatabaseUrl(databaseUrl));
    }

    var configuredConnection = configuration.GetConnectionString("DefaultConnection")?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredConnection) && LooksLikePostgresConnection(configuredConnection))
    {
        return new DatabaseConfiguration("postgres", configuredConnection);
    }

    return new DatabaseConfiguration(
        "sqlite",
        string.IsNullOrWhiteSpace(configuredConnection) ? "Data Source=trading.db" : configuredConnection);
}

static bool ShouldBypassProxy()
{
    var proxyCandidates = new[]
    {
        Environment.GetEnvironmentVariable("HTTPS_PROXY"),
        Environment.GetEnvironmentVariable("HTTP_PROXY"),
        Environment.GetEnvironmentVariable("ALL_PROXY")
    };

    return proxyCandidates.Any(IsBlackholeLoopbackProxy);
}

static bool IsBlackholeLoopbackProxy(string? proxyValue)
{
    if (string.IsNullOrWhiteSpace(proxyValue) || !Uri.TryCreate(proxyValue, UriKind.Absolute, out var proxyUri))
    {
        return false;
    }

    if (proxyUri.Port != 9)
    {
        return false;
    }

    if (string.Equals(proxyUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(proxyUri.Host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
}

static bool LooksLikePostgresConnection(string connectionString)
{
    return connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
           connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
           connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
           connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
}

static string ConvertDatabaseUrl(string databaseUrl)
{
    if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
        databaseUrl.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException("DATABASE_URL is not a valid absolute URI.");
    }

    var userInfoParts = uri.UserInfo.Split(':', 2);
    var username = userInfoParts.Length > 0 ? Uri.UnescapeDataString(userInfoParts[0]) : string.Empty;
    var password = userInfoParts.Length > 1 ? Uri.UnescapeDataString(userInfoParts[1]) : string.Empty;
    var databaseName = uri.AbsolutePath.Trim('/');

    var builder = new StringBuilder()
        .Append("Host=").Append(uri.Host).Append(';')
        .Append("Port=").Append(uri.Port > 0 ? uri.Port : 5432).Append(';')
        .Append("Database=").Append(databaseName).Append(';')
        .Append("Username=").Append(username).Append(';')
        .Append("Password=").Append(password).Append(';');

    var query = uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var hasSslMode = false;
    var hasTrustServerCertificate = false;

    foreach (var pair in query)
    {
        var parts = pair.Split('=', 2);
        if (parts.Length != 2)
        {
            continue;
        }

        var key = Uri.UnescapeDataString(parts[0]);
        var value = Uri.UnescapeDataString(parts[1]);

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("SSL Mode=").Append(value).Append(';');
            hasSslMode = true;
            continue;
        }

        if (key.Equals("trust server certificate", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("trust_server_certificate", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Trust Server Certificate=").Append(value).Append(';');
            hasTrustServerCertificate = true;
        }
    }

    if (!hasSslMode)
    {
        builder.Append("SSL Mode=Require;");
    }

    if (!hasTrustServerCertificate)
    {
        builder.Append("Trust Server Certificate=True;");
    }

    return builder.ToString();
}

internal sealed record DatabaseConfiguration(string Provider, string ConnectionString);
