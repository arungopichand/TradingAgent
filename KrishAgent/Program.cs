using KrishAgent.Configuration;
using KrishAgent.Services;
using Microsoft.EntityFrameworkCore;
using KrishAgent.Data;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpClient<MarketService>();
builder.Services.AddScoped<IndicatorService>();
builder.Services.AddHttpClient<AIService>();
builder.Services.AddScoped<DataService>();
builder.Services.AddScoped<IntradayTradingService>();
builder.Services.AddScoped<PennyStockTradingService>();
builder.Services.AddHostedService<TradingDataService>();
builder.Services.AddDbContext<KrishAgent.Data.TradingContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:3001",
                "http://127.0.0.1:3000",
                "http://127.0.0.1:3001")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

await using (var startupScope = app.Services.CreateAsyncScope())
{
    var dbContext = startupScope.ServiceProvider.GetRequiredService<TradingContext>();
    var startupLogger = startupScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    try
    {
        await dbContext.Database.MigrateAsync();
        startupLogger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to apply database migrations");
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
