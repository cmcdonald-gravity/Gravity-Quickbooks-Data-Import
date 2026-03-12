
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Bind options
builder.Services.Configure<QuickBooksOptions>(builder.Configuration.GetSection("QuickBooks"));
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseHttpsRedirection();

// ---- Durable token store (JSON file on disk) ----
var tokenStore = new FileTokenStore(Path.Combine(app.Environment.ContentRootPath, "tokens.json"));

// Health-check root
app.MapGet("/", () => Results.Text("QbAuthServr is running. Try /auth/quickbooks", "text/plain"));

// Step 1: Begin OAuth — redirect to Intuit
app.MapGet("/auth/quickbooks", (HttpContext ctx, IOptions<QuickBooksOptions> opt) =>
{
    var o = opt.Value;

    // CSRF protection: random state stored in a secure cookie
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    ctx.Response.Cookies.Append(
        "qb_state",
        state,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

    // Build authorize URL
    var qs = new QueryBuilder(new Dictionary<string, string?>
    {
        ["client_id"] = o.ClientId,
        ["response_type"] = "code",
        ["scope"] = o.Scopes,
        ["redirect_uri"] = o.RedirectUri,
        ["state"] = state
    });

    var authorizeUrl = $"{QuickBooksOAuth.AuthorizationEndpoint}{qs.ToQueryString()}";
    return Results.Redirect(authorizeUrl);
});

// Step 2: OAuth callback — exchange code for tokens
app.MapGet("/auth/quickbooks/callback", async (HttpContext ctx, IOptions<QuickBooksOptions> opt, IHttpClientFactory httpFactory) =>
{
    var o = opt.Value;

    var query = ctx.Request.Query;
    var code = query["code"].ToString();
    var state = query["state"].ToString();
    var realmId = query["realmId"].ToString(); // QuickBooks company ID

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        return Results.BadRequest("Missing code/state.");

    // Validate state against cookie
    if (!ctx.Request.Cookies.TryGetValue("qb_state", out var cookieState) || cookieState != state)
        return Results.BadRequest("Invalid state.");

    // Clear one-time state cookie
    ctx.Response.Cookies.Delete("qb_state");

    var client = httpFactory.CreateClient();

    // Basic auth header with clientId:clientSecret (base64)
    var creds = $"{o.ClientId}:{o.ClientSecret}";
    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(creds));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

    // Token request
    var form = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = o.RedirectUri
    };

    using var content = new FormUrlEncodedContent(form);
    var tokenResponse = await client.PostAsync(QuickBooksOAuth.TokenEndpoint, content);

    var raw = await tokenResponse.Content.ReadAsStringAsync();
    if (!tokenResponse.IsSuccessStatusCode)
    {
        app.Logger.LogError("Token exchange failed: {Status} - {Body}", tokenResponse.StatusCode, raw);
        return Results.Problem("Token exchange failed. Check server logs.");
    }

    var tokens = JsonSerializer.Deserialize<TokenResponse>(raw, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? new TokenResponse();

    // Save tokens durably to disk
    await tokenStore.SaveAsync(realmId, tokens);

    // Simple confirmation page
    var html = $@"
<!doctype html>
<html>
  <head><meta charset=""utf-8""><title>QuickBooks Connected</title></head>
  <body style=""font-family: system-ui, -apple-system, Segoe UI, Roboto, Arial"">
    <h2>✅ Connected to QuickBooks</h2>
    <p>Realm ID (company): <code>{System.Net.WebUtility.HtmlEncode(realmId)}</code></p>
    <p>Tokens were saved to <code>tokens.json</code>.</p>
  </body>
</html>";
    return Results.Content(html, "text/html");
});

// (Optional) Example API call using the stored token (CompanyInfo)
app.MapGet("/api/companyinfo", async (string realmId, IHttpClientFactory httpFactory) =>
{
    var tokens = await tokenStore.GetAsync(realmId);
    if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        return Results.Unauthorized();

    // Example: QuickBooks v3 CompanyInfo endpoint (sandbox base URL shown)
    var url = $"https://sandbox-quickbooks.api.intuit.com/v3/company/{realmId}/companyinfo/{realmId}?minorversion=65";

    var client = httpFactory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    var resp = await client.GetAsync(url);
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Text(body, "application/json");
});

app.Run();


// ----------------- Types below (inline so there are no missing references) -----------------

sealed class FileTokenStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileTokenStore(string filePath)
    {
        _filePath = filePath;
        EnsureFile();
    }

    private void EnsureFile()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "{}");
    }

    public Task SaveAsync(string realmId, TokenResponse token)
    {
        realmId ??= "";

        lock (_lock)
        {
            var json = File.ReadAllText(_filePath);
            var map = JsonSerializer.Deserialize<Dictionary<string, TokenResponse>>(json)
                ?? new Dictionary<string, TokenResponse>(StringComparer.OrdinalIgnoreCase);

            map[realmId] = token;

            File.WriteAllText(_filePath, JsonSerializer.Serialize(map, JsonOpts));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the token for the given realmId, or null if not found.
    /// </summary>
    public Task<TokenResponse?> GetAsync(string realmId)
    {
        realmId ??= "";

        TokenResponse? token = null;

        lock (_lock)
        {
            var json = File.ReadAllText(_filePath);
            var map = JsonSerializer.Deserialize<Dictionary<string, TokenResponse>>(json)
                ?? new Dictionary<string, TokenResponse>(StringComparer.OrdinalIgnoreCase);

            map.TryGetValue(realmId, out token);
        }

        return Task.FromResult(token);
    }
}

sealed class TokenResponse
{
    public string TokenType { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public int ExpiresIn { get; set; }           // seconds
    public string RefreshToken { get; set; } = "";
    public int XRefreshTokenExpiresIn { get; set; } // seconds
    public string IdToken { get; set; } = "";    // present if OpenID scopes used
}

sealed class QuickBooksOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string Environment { get; init; } = "sandbox";
    public string Scopes { get; init; } = "com.intuit.quickbooks.accounting openid profile email";
}

static class QuickBooksOAuth
{
    public static string AuthorizationEndpoint => "https://appcenter.intuit.com/connect/oauth2";
    public static string TokenEndpoint => "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
}