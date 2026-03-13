using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QbAuthServr.Options;
using QbAuthServr.Services.Auth;

namespace QbAuthServr.Controllers;

[ApiController]
public sealed class AuthController : ControllerBase
{
    private readonly QuickBooksAuthService _auth;
    private readonly IStateStore _state;
    private readonly QuickBooksOptions _opt;

    public AuthController(QuickBooksAuthService auth, IStateStore state, IOptions<QuickBooksOptions> opt)
    {
        _auth = auth;
        _state = state;
        _opt = opt.Value;
    }

    [HttpGet("/auth/quickbooks")]
    public IActionResult Start()
    {
        var state = _state.Create();
        var url = _auth.BuildAuthorizeUrl(state);
        return Redirect(url);
    }

    [HttpGet("/auth/quickbooks/callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, [FromQuery] string realmId)
    {
        if (!_state.ValidateAndConsume(state))
            return BadRequest("Invalid state");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(realmId))
            return BadRequest("Missing code/realmId");

        try
        {
            await _auth.ExchangeCodeForTokensAsync(realmId, code);
            return Redirect($"/?connected=1&realmId={Uri.EscapeDataString(realmId)}");
        }
        catch (Exception ex)
        {
            return Problem("Token exchange failed:\n" + ex.Message);
        }
    }
}