using Microsoft.AspNetCore.Mvc;
using QbAuthServr.Services.Api;

namespace QbAuthServr.Controllers;

[ApiController]
public sealed class AccountsController : ControllerBase
{
    private readonly QuickBooksApiService _api;

    public AccountsController(QuickBooksApiService api) => _api = api;

    [HttpGet("/api/accounts")]
    public async Task<IActionResult> Get([FromQuery] string realmId)
    {
        var (ok, err, rows) = await _api.GetAccountsAsync(realmId);
        if (!ok) return Problem("Accounts query failed:\n" + err);
        return Ok(rows ?? new());
    }
}