using Microsoft.AspNetCore.Mvc;
using QbAuthServr.Services.Api;
using QbAuthServr.Services.Excel;

namespace QbAuthServr.Controllers;

[ApiController]
public sealed class ExportController : ControllerBase
{
    private readonly QuickBooksApiService _api;
    private readonly VoucherExportService _voucherExporter;
    private readonly ChartOfAccountsExportService _coaExporter;
    private readonly IWebHostEnvironment _env;

    public ExportController(
        QuickBooksApiService api,
        VoucherExportService voucherExporter,
        ChartOfAccountsExportService coaExporter,
        IWebHostEnvironment env)
    {
        _api = api;
        _voucherExporter = voucherExporter;
        _coaExporter = coaExporter;
        _env = env;
    }

    [HttpGet("/api/export-vouchers")]
    public async Task<IActionResult> ExportVouchers([FromQuery] string realmId)
    {
        var (ok, err, rows) = await _api.GetBillsAsync(realmId);
        if (!ok) return Problem("Could not retrieve bills:\n" + err);

        var (bytes, fileName) = _voucherExporter.BuildWorkbook(rows ?? new(), _env.WebRootPath);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpGet("/api/export-accounts")]
    public async Task<IActionResult> ExportAccounts([FromQuery] string realmId)
    {
        var (ok, err, rows) = await _api.GetAccountsAsync(realmId);
        if (!ok) return Problem("Could not retrieve accounts:\n" + err);

        var (bytes, fileName) = _coaExporter.BuildWorkbook(rows ?? new(), _env.WebRootPath);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
