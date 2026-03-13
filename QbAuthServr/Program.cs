using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseHttpsRedirection();

// Serve UI from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

//
// ---------------------------------------------------------
// CONFIG
// ---------------------------------------------------------
var qb = builder.Configuration.GetSection("QuickBooks");
var QB_CLIENT_ID     = qb["ClientId"]     ?? "";
var QB_CLIENT_SECRET = qb["ClientSecret"] ?? "";
var QB_REDIRECT      = qb["RedirectUri"]  ?? "https://localhost:5148/auth/quickbooks/callback";
var QB_SCOPES        = qb["Scopes"]       ?? "com.intuit.quickbooks.accounting openid profile email";

const string AUTH_URL  = "https://appcenter.intuit.com/connect/oauth2";
const string TOKEN_URL = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";

const string PROD_HOST    = "quickbooks.api.intuit.com";
const string SANDBOX_HOST = "sandbox-quickbooks.api.intuit.com";

//
// ---------------------------------------------------------
// REALM STORE (tokens + chosen host)
// ---------------------------------------------------------
var realmStore = new FileRealmStore(
    Path.Combine(app.Environment.ContentRootPath, "tokens.json")
);

//
// ---------------------------------------------------------
// STATE STORE (server-side, no cookies)
// ---------------------------------------------------------
static string NewState() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

var StateStore = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

//
// ---------------------------------------------------------
// HEALTH
// ---------------------------------------------------------
app.MapGet("/health", () => "OK");

//
// ---------------------------------------------------------
// 1) START OAUTH
// ---------------------------------------------------------
app.MapGet("/auth/quickbooks", () =>
{
    var state = NewState();
    StateStore[state] = 0;

    var url =
        $"{AUTH_URL}" +
        $"?client_id={Uri.EscapeDataString(QB_CLIENT_ID)}" +
        $"&response_type=code" +
        $"&scope={Uri.EscapeDataString(QB_SCOPES)}" +
        $"&redirect_uri={Uri.EscapeDataString(QB_REDIRECT)}" +
        $"&state={Uri.EscapeDataString(state)}";

    return Results.Redirect(url);
});

//
// ---------------------------------------------------------
// 2) CALLBACK
// ---------------------------------------------------------
app.MapGet("/auth/quickbooks/callback", async (
    HttpContext ctx,
    IHttpClientFactory httpFactory) =>
{
    string code    = ctx.Request.Query["code"];
    string state   = ctx.Request.Query["state"];
    string realmId = ctx.Request.Query["realmId"];

    if (string.IsNullOrWhiteSpace(code) ||
        string.IsNullOrWhiteSpace(state))
        return Results.BadRequest("Missing code/state");

    // state validation
    if (!StateStore.TryGetValue(state, out var stVal))
    {
        var existing = await realmStore.GetAsync(realmId);
        if (existing is not null)
            return Results.Redirect($"/?connected=1&realmId={realmId}");

        return Results.BadRequest("Invalid state");
    }

    // idempotent
    if (stVal == 1)
    {
        var existing = await realmStore.GetAsync(realmId);
        if (existing is not null)
            return Results.Redirect($"/?connected=1&realmId={realmId}");
    }

    var http = httpFactory.CreateClient();
    var basic = Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{QB_CLIENT_ID}:{QB_CLIENT_SECRET}")
    );

    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", basic);

    var form = new Dictionary<string, string>
    {
        ["grant_type"]   = "authorization_code",
        ["code"]         = code,
        ["redirect_uri"] = QB_REDIRECT
    };

    var resp = await http.PostAsync(TOKEN_URL, new FormUrlEncodedContent(form));
    var raw  = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
        return Results.Problem("Token exchange failed:\n" + raw);

    var tokens = JsonSerializer.Deserialize<TokenResponse>(raw, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    if (tokens == null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        return Results.Problem("Token empty:\n" + raw);

    if (string.IsNullOrWhiteSpace(realmId))
        return Results.Problem("QuickBooks did not return realmId. Accounting scope needed.");

    await realmStore.SaveAsync(realmId, new RealmAuth { Tokens = tokens, ApiHost = null });

    StateStore[state] = 1;
    _ = StateStore.TryRemove(state, out _);

    return Results.Redirect($"/?connected=1&realmId={realmId}");
});

app.MapGet("/api/bills", async (
    string realmId,
    IHttpClientFactory httpFactory) =>
{
    var auth = await realmStore.GetAsync(realmId);
    if (auth is null || string.IsNullOrWhiteSpace(auth.Tokens.AccessToken))
        return Results.Unauthorized();

    var http = httpFactory.CreateClient();

    async Task<(bool ok, HttpStatusCode code, string body)> QueryBills(string host, string token)
    {
        string query =
            "SELECT Id, DocNumber, TxnDate, TotalAmt, PrivateNote, VendorRef " +
            "FROM Bill MAXRESULTS 200";

        string url = $"https://{host}/v3/company/{realmId}/query";

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(query, Encoding.UTF8, "application/text")
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/json");

        var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        return (resp.IsSuccessStatusCode, resp.StatusCode, body);
    }

    async Task<bool> RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(auth.Tokens.RefreshToken))
            return false;

        var tokenClient = httpFactory.CreateClient();
        string basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{QB_CLIENT_ID}:{QB_CLIENT_SECRET}")
        );
        tokenClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", basic);

        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = auth.Tokens.RefreshToken
        };

        var r = await tokenClient.PostAsync(TOKEN_URL, new FormUrlEncodedContent(form));
        var raw = await r.Content.ReadAsStringAsync();

        if (!r.IsSuccessStatusCode)
            return false;

        var updated = JsonSerializer.Deserialize<TokenResponse>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (updated == null || string.IsNullOrWhiteSpace(updated.AccessToken))
            return false;

        auth.Tokens = updated;
        await realmStore.SaveAsync(realmId, auth);
        return true;
    }

    string[] hostsToTry =
        auth.ApiHost switch
        {
            PROD_HOST    => new[] { PROD_HOST, SANDBOX_HOST },
            SANDBOX_HOST => new[] { SANDBOX_HOST, PROD_HOST },
            _            => new[] { PROD_HOST, SANDBOX_HOST }
        };

    List<BillRow> bills = new();
    string? workingHost = null;

    foreach (var host in hostsToTry)
    {
        var (ok, code, body) = await QueryBills(host, auth.Tokens.AccessToken);

        if (!ok && code == HttpStatusCode.Unauthorized)
        {
            var refreshed = await RefreshAsync();
            if (refreshed)
                (ok, code, body) = await QueryBills(host, auth.Tokens.AccessToken);
        }

        if (!ok)
        {
            if (host == hostsToTry[^1])
                return Results.Problem("Bills query failed:\n" + body);
            continue;
        }

        workingHost = host;

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
            !qr.TryGetProperty("Bill", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            break;
        }

        foreach (var b in arr.EnumerateArray())
        {
            bills.Add(new BillRow
            {
                Id        = b.TryGetProperty("Id", out var vId) ? vId.GetString() ?? "" : "",
                DocNumber = b.TryGetProperty("DocNumber", out var vDoc) ? vDoc.GetString() ?? "" : "",
                TxnDate   = b.TryGetProperty("TxnDate", out var vDate) ? vDate.GetString() ?? "" : "",
                TotalAmt  = b.TryGetProperty("TotalAmt", out var vTot) && vTot.TryGetDecimal(out var dTot) ? dTot : 0,
                Memo      = b.TryGetProperty("PrivateNote", out var vNote) ? vNote.GetString() ?? "" : "",
                VendorName = (b.TryGetProperty("VendorRef", out var vRef) &&
                             vRef.TryGetProperty("name", out var vName))
                            ? vName.GetString() ?? ""
                            : ""
            });
        }

        break;
    }

    if (!string.IsNullOrWhiteSpace(workingHost) &&
        auth.ApiHost != workingHost)
    {
        auth.ApiHost = workingHost;
        await realmStore.SaveAsync(realmId, auth);
    }

    return Results.Json(bills);
});

app.Run();

//
// ---------------------------------------------------------
// SUPPORT TYPES
// ---------------------------------------------------------
sealed class FileRealmStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public FileRealmStore(string path)
    {
        _path = path;
        if (!File.Exists(_path))
            File.WriteAllText(_path, "{}");
    }

    public Task SaveAsync(string? realmId, RealmAuth data)
    {
        realmId ??= "";
        lock (_lock)
        {
            var json = File.ReadAllText(_path);
            Dictionary<string, RealmAuth>? map = null;

            try { map = JsonSerializer.Deserialize<Dictionary<string, RealmAuth>>(json); }
            catch { }

            map ??= new Dictionary<string, RealmAuth>(StringComparer.OrdinalIgnoreCase);

            map[realmId] = data;

            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        return Task.CompletedTask;
    }

    public Task<RealmAuth?> GetAsync(string? realmId)
    {
        realmId ??= "";
        lock (_lock)
        {
            var json = File.ReadAllText(_path);
            Dictionary<string, RealmAuth>? map = null;

            try { map = JsonSerializer.Deserialize<Dictionary<string, RealmAuth>>(json); }
            catch { }

            map ??= new Dictionary<string, RealmAuth>(StringComparer.OrdinalIgnoreCase);

            map.TryGetValue(realmId, out var auth);
            return Task.FromResult(auth);
        }
    }
}

sealed class RealmAuth
{
    public TokenResponse Tokens { get; set; } = new();
    public string? ApiHost { get; set; }
}

sealed class TokenResponse
{
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("x_refresh_token_expires_in")]
    public int XRefreshTokenExpiresIn { get; set; }

    [JsonPropertyName("id_token")]
    public string IdToken { get; set; } = "";
}

sealed class BillRow
{
    public string Id { get; set; } = "";
    public string DocNumber { get; set; } = "";
    public string TxnDate { get; set; } = "";
    public decimal TotalAmt { get; set; }
    public string Memo { get; set; } = "";
    public string VendorName { get; set; } = "";
}