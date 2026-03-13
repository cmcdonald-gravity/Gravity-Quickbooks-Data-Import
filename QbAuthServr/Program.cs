using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

// ===============================
// OPTIONS STORED HERE (NO OTHER FILE)
// ===============================
var qbSection = builder.Configuration.GetSection("QuickBooks");
var QB_CLIENT_ID = qbSection["ClientId"] ?? "";
var QB_CLIENT_SECRET = qbSection["ClientSecret"] ?? "";
var QB_REDIRECT = qbSection["RedirectUri"] ?? "";    // e.g. https://localhost:5148/auth/quickbooks/callback
var QB_SCOPES = qbSection["Scopes"] ??
    "com.intuit.quickbooks.accounting openid profile email";

// ===============================
// CONSTANTS
// ===============================
const string AUTH_URL = "https://appcenter.intuit.com/connect/oauth2";
const string TOKEN_URL = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";

// YOUR DESKTOP HTML PATH (REDIRECT TARGET)
const string DESKTOP_INDEX =
"file:///C:/Users/caden/OneDrive/Desktop/Gravity%20Quickbooks%20Data%20Import/index.html";

// ===============================
// START APP
// ===============================
var app = builder.Build();

app.UseHttpsRedirection();


// ==========================================================
// SIMPLE TOKEN STORE → tokens.json
// ==========================================================
var tokenStore = new FileTokenStore(Path.Combine(app.Environment.ContentRootPath, "tokens.json"));


// ==========================================================
// HEALTH CHECK
// ==========================================================
app.MapGet("/health", () => "OK");


// ==========================================================
// 1) START QUICKBOOKS LOGIN
// ==========================================================
app.MapGet("/auth/quickbooks", (HttpContext ctx) =>
{
    // generate anti-forgery state
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    ctx.Response.Cookies.Append("qb_state", state, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax
    });

    var redirect =
        $"{AUTH_URL}" +
        $"?client_id={QB_CLIENT_ID}" +
        $"&response_type=code" +
        $"&scope={Uri.EscapeDataString(QB_SCOPES)}" +
        $"&redirect_uri={Uri.EscapeDataString(QB_REDIRECT)}" +
        $"&state={state}";

    return Results.Redirect(redirect);
});


// ==========================================================
// 2) CALLBACK AFTER LOGIN
// ==========================================================
app.MapGet("/auth/quickbooks/callback", async (
    HttpContext ctx,
    IHttpClientFactory httpFactory) =>
{
    string code = ctx.Request.Query["code"];
    string state = ctx.Request.Query["state"];
    string realmId = ctx.Request.Query["realmId"];

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        return Results.BadRequest("Missing code/state.");

    // Check state cookie
    if (!ctx.Request.Cookies.TryGetValue("qb_state", out var cookieState) ||
        cookieState != state)
        return Results.BadRequest("Invalid state.");

    ctx.Response.Cookies.Delete("qb_state");

    var client = httpFactory.CreateClient();

    // BASIC AUTH (ID:SECRET)
    var combo = $"{QB_CLIENT_ID}:{QB_CLIENT_SECRET}";
    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(combo));
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", basic);

    var data = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = QB_REDIRECT
    };

    var response = await client.PostAsync(TOKEN_URL, new FormUrlEncodedContent(data));
    var raw = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return Results.Problem("Token exchange failed:\n" + raw);

    var tokens = JsonSerializer.Deserialize<TokenResponse>(raw,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? new TokenResponse();

    // SAVE TOKENS FOR THIS COMPANY
    await tokenStore.SaveAsync(realmId, tokens);

    // ======================================================
    // REDIRECT USER BACK TO YOUR DESKTOP HTML FILE
    // ======================================================
    string finalUrl =
        $"{DESKTOP_INDEX}?connected=1&realmId={Uri.EscapeDataString(realmId ?? "")}";

    return Results.Redirect(finalUrl);
});


// ==========================================================
// 3) IMPORT TRANSACTIONS (CALLED BY YOUR HTML FORM)
// ==========================================================
app.MapPost("/api/import-transactions", async (HttpContext ctx) =>
{
    if (!ctx.Request.HasFormContentType)
        return Results.BadRequest("Expected form-data");

    var form = await ctx.Request.ReadFormAsync();
    var realmId = form["realmId"].ToString();

    var token = await tokenStore.GetAsync(realmId);
    if (token == null)
        return Results.Unauthorized();

    return Results.Text($"Import started for realm {realmId}");
});


// ==========================================================
// RUN SERVER
// ==========================================================
app.Run();


// ==========================================================
// SUPPORT CLASSES (NO OTHER .CS FILES REQUIRED)
// ==========================================================
sealed class FileTokenStore
{
    private readonly string _file;
    private readonly object _lockObj = new();

    public FileTokenStore(string path)
    {
        _file = path;
        if (!File.Exists(_file))
            File.WriteAllText(_file, "{}");
    }

    public Task SaveAsync(string realmId, TokenResponse token)
    {
        lock (_lockObj)
        {
            var json = File.ReadAllText(_file);
            var map = JsonSerializer.Deserialize<Dictionary<string, TokenResponse>>(json)
                      ?? new();

            map[realmId ?? ""] = token;

            File.WriteAllText(_file,
                JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }

        return Task.CompletedTask;
    }

    public Task<TokenResponse?> GetAsync(string realmId)
    {
        lock (_lockObj)
        {
            var json = File.ReadAllText(_file);
            var map = JsonSerializer.Deserialize<Dictionary<string, TokenResponse>>(json)
                      ?? new();

            map.TryGetValue(realmId ?? "", out var token);
            return Task.FromResult<TokenResponse?>(token);
        }
    }
}

sealed class TokenResponse
{
    public string TokenType { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = "";
    public int XRefreshTokenExpiresIn { get; set; }
    public string IdToken { get; set; } = "";
}