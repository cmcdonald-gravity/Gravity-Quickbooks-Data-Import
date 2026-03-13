using Microsoft.AspNetCore.Mvc;
using QbAuthServr.Services.Api;

namespace QbAuthServr.Controllers;

[ApiController]
public sealed class BillsController : ControllerBase
{
    private readonly QuickBooksApiService _api;

    public BillsController(QuickBooksApiService api) => _api = api;

    [HttpGet("/api/bills")]
    public async Task<IActionResult> Get([FromQuery] string realmId)
    {
        var (ok, err, rows) = await _api.GetBillsAsync(realmId);
        if (!ok) return Problem("Bills query failed:\n" + err);
        return Ok(rows ?? new());
    }
}