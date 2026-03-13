using QbAuthServr.Options;
using QbAuthServr.Services.Api;
using QbAuthServr.Services.Auth;
using QbAuthServr.Services.Excel;
using QbAuthServr.Services.Storage;

var builder = WebApplication.CreateBuilder(args);

// Controllers + JSON (default camelCase; your app.js tolerates both)
builder.Services.AddControllers();

// Options
builder.Services.Configure<QuickBooksOptions>(builder.Configuration.GetSection("QuickBooks"));

// HttpClient
builder.Services.AddHttpClient();

// Services
builder.Services.AddSingleton<IStateStore, InMemoryStateStore>();
builder.Services.AddSingleton<ITokenStore, FileRealmStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var path = Path.Combine(env.ContentRootPath, "tokens.json");
    return new FileRealmStore(path);
});
builder.Services.AddTransient<QuickBooksAuthService>();
builder.Services.AddTransient<QuickBooksApiService>();
builder.Services.AddTransient<VoucherExportService>();
builder.Services.AddTransient<ChartOfAccountsExportService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Simple health
app.MapGet("/health", () => Results.Text("OK", "text/plain"));

app.Run();