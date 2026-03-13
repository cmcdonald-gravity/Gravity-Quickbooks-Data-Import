using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QbAuthServr.Models;
using QbAuthServr.Options;
using QbAuthServr.Services.Auth;
using QbAuthServr.Services.Storage;

namespace QbAuthServr.Services.Api;

public sealed class QuickBooksApiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ITokenStore _tokenStore;
    private readonly QuickBooksAuthService _authService;
    private readonly QuickBooksOptions _opt;

    public QuickBooksApiService(
        IHttpClientFactory httpFactory,
        ITokenStore tokenStore,
        QuickBooksAuthService authService,
        IOptions<QuickBooksOptions> opt)
    {
        _httpFactory = httpFactory;
        _tokenStore = tokenStore;
        _authService = authService;
        _opt = opt.Value;
    }

    // ---------- Bills (production-only; minimal, known-good) ----------
    private const string ProdHost = "quickbooks.api.intuit.com";
    private const string MinimalBillQuery = @"
SELECT Id, DocNumber, TxnDate, TotalAmt, PrivateNote, VendorRef
FROM Bill MAXRESULTS 1000";

    public async Task<(bool ok, string errorOrEmpty, List<BillRow>? rows)> GetBillsAsync(string realmId)
    {
        var auth = await _tokenStore.GetAsync(realmId);
        if (auth is null || string.IsNullOrWhiteSpace(auth.Tokens.AccessToken))
            return (false, "Unauthorized", null);

        async Task<(bool ok, HttpStatusCode code, string body)> CallAsync()
        {
            var url = $"https://{ProdHost}/v3/company/{realmId}/query";
            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", auth.Tokens.AccessToken);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(MinimalBillQuery, Encoding.UTF8, "application/text")
            };

            var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, resp.StatusCode, body);
        }

        var (ok, code, body) = await CallAsync();
        if (!ok && code == HttpStatusCode.Unauthorized && await _authService.RefreshAsync(realmId))
            (ok, code, body) = await CallAsync();

        if (!ok)
            return (false, body, null);

        var rows = new List<BillRow>();
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
            !qr.TryGetProperty("Bill", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return (true, "", rows); // empty set is fine
        }

        foreach (var b in arr.EnumerateArray())
        {
            string id      = b.TryGetProperty("Id", out var vId) ? vId.GetString() ?? "" : "";
            string docNo   = b.TryGetProperty("DocNumber", out var vDoc) ? vDoc.GetString() ?? "" : "";
            string txnDate = b.TryGetProperty("TxnDate", out var vDt) ? vDt.GetString() ?? "" : "";
            decimal total  = b.TryGetProperty("TotalAmt", out var vTot) && vTot.TryGetDecimal(out var dTot) ? dTot : 0m;
            string memo    = b.TryGetProperty("PrivateNote", out var vMemo) ? vMemo.GetString() ?? "" : "";

            string vendorName = "";
            string vendorId   = "";
            if (b.TryGetProperty("VendorRef", out var vRef))
            {
                if (vRef.TryGetProperty("name", out var vn)) vendorName = vn.GetString() ?? "";
                if (vRef.TryGetProperty("value", out var vv)) vendorId = vv.GetString() ?? "";
            }

            rows.Add(new BillRow
            {
                Id = id,
                DocNumber = docNo,
                TxnDate = txnDate,
                TotalAmt = total,
                Memo = memo,
                VendorName = vendorName,
                VendorId = vendorId
            });
        }

        return (true, "", rows);
    }

    // ---------- Chart of Accounts (production-only; minimal, safe) ----------
    private const string MinimalAccountQuery = @"
SELECT Id, Name, AccountType, AccountSubType, Active, AcctNum, FullyQualifiedName
FROM Account MAXRESULTS 1000";

    public async Task<(bool ok, string errorOrEmpty, List<AccountRow>? rows)> GetAccountsAsync(string realmId)
    {
        var auth = await _tokenStore.GetAsync(realmId);
        if (auth is null || string.IsNullOrWhiteSpace(auth.Tokens.AccessToken))
            return (false, "Unauthorized", null);

        async Task<(bool ok, HttpStatusCode code, string body)> CallAsync()
        {
            var url = $"https://{ProdHost}/v3/company/{realmId}/query";
            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", auth.Tokens.AccessToken);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(MinimalAccountQuery, Encoding.UTF8, "application/text")
            };

            var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, resp.StatusCode, body);
        }

        var (ok, code, body) = await CallAsync();
        if (!ok && code == HttpStatusCode.Unauthorized && await _authService.RefreshAsync(realmId))
            (ok, code, body) = await CallAsync();

        if (!ok)
            return (false, body, null);

        var rows = new List<AccountRow>();
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
            !qr.TryGetProperty("Account", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return (true, "", rows);
        }

        foreach (var a in arr.EnumerateArray())
        {
            var row = new AccountRow
            {
                Id = a.TryGetProperty("Id", out var vId) ? vId.GetString() ?? "" : "",
                Name = a.TryGetProperty("Name", out var vName) ? vName.GetString() ?? "" : "",
                AccountType = a.TryGetProperty("AccountType", out var vType) ? vType.GetString() ?? "" : "",
                AccountSubType = a.TryGetProperty("AccountSubType", out var vSub) ? vSub.GetString() ?? "" : "",
                Active = a.TryGetProperty("Active", out var vAct) && vAct.ValueKind == JsonValueKind.True,
                AcctNum = a.TryGetProperty("AcctNum", out var vNum) ? vNum.GetString() ?? "" : "",
                FullyQualifiedName = a.TryGetProperty("FullyQualifiedName", out var vFq) ? vFq.GetString() ?? "" : ""
            };
            rows.Add(row);
        }

        return (true, "", rows);
    }
}