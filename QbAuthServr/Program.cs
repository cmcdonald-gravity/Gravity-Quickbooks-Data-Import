using QbAuthServr.Options;
using QbAuthServr.Services.Api;
using QbAuthServr.Services.Auth;
using QbAuthServr.Services.Excel;
using QbAuthServr.Services.Storage;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Options
builder.Services.Configure<QuickBooksOptions>(builder.Configuration.GetSection("QuickBooks"));

// HttpClient
builder.Services.AddHttpClient();

// Services
builder.Services.AddSingleton<IStateStore, InMemoryStateStore>();
builder.Services.AddSingleton<ITokenStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    // Persistent data directory (set via DATA_DIR on Render; fallback to ./data locally)
    var dataRoot = Environment.GetEnvironmentVariable("DATA_DIR");
    if (string.IsNullOrWhiteSpace(dataRoot))
        dataRoot = Path.Combine(env.ContentRootPath, "data");

    Directory.CreateDirectory(dataRoot);
    var tokensPath = Path.Combine(dataRoot, "tokens.json");
    return new FileRealmStore(tokensPath);
});

builder.Services.AddTransient<QuickBooksAuthService>();
builder.Services.AddTransient<QuickBooksApiService>();
builder.Services.AddTransient<VoucherExportService>();
builder.Services.AddTransient<ChartOfAccountsExportService>();

var app = builder.Build();

// Reverse-proxy correctness (Front Door/Render/NGINX)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health
app.MapGet("/health", () => Results.Text("OK", "text/plain"));

app.Run();