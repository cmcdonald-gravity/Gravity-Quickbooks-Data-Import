using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using QbAuthServr.Models;
using QbAuthServr.Options;
using QbAuthServr.Services.Storage;

namespace QbAuthServr.Services.Auth;

public sealed class QuickBooksAuthService
{
    private const string AUTH_URL  = "https://appcenter.intuit.com/connect/oauth2";
    private const string TOKEN_URL = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";

    private readonly QuickBooksOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ITokenStore _tokenStore;

    public QuickBooksAuthService(IOptions<QuickBooksOptions> opt, IHttpClientFactory httpFactory, ITokenStore tokenStore)
    {
        _opt = opt.Value;
        _httpFactory = httpFactory;
        _tokenStore = tokenStore;
    }

    public string BuildAuthorizeUrl(string state)
    {
        var url =
            $"{AUTH_URL}" +
            $"?client_id={Uri.EscapeDataString(_opt.ClientId)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(_opt.Scopes)}" +
            $"&redirect_uri={Uri.EscapeDataString(_opt.RedirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}";
        return url;
    }

    public async Task ExchangeCodeForTokensAsync(string realmId, string code)
    {
        var http = _httpFactory.CreateClient();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var form = new Dictionary<string, string>
        {
            ["grant_type"]   = "authorization_code",
            ["code"]         = code,
            ["redirect_uri"] = _opt.RedirectUri
        };

        var resp = await http.PostAsync(TOKEN_URL, new FormUrlEncodedContent(form));
        var raw  = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("Token exchange failed:\n" + raw);

        var tokens = JsonSerializer.Deserialize<TokenResponse>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new TokenResponse();

        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
            throw new InvalidOperationException("Empty access token:\n" + raw);

        await _tokenStore.SaveAsync(realmId, new RealmAuth { Tokens = tokens, ApiHost = null });
    }

    public async Task<bool> RefreshAsync(string realmId)
    {
        var auth = await _tokenStore.GetAsync(realmId);
        if (auth is null || string.IsNullOrWhiteSpace(auth.Tokens.RefreshToken)) return false;

        var http = _httpFactory.CreateClient();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = auth.Tokens.RefreshToken
        };

        var resp = await http.PostAsync(TOKEN_URL, new FormUrlEncodedContent(form));
        var raw  = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode) return false;

        var updated = JsonSerializer.Deserialize<TokenResponse>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (updated is null || string.IsNullOrWhiteSpace(updated.AccessToken)) return false;

        auth.Tokens = updated;
        await _tokenStore.SaveAsync(realmId, auth);
        return true;
    }
}