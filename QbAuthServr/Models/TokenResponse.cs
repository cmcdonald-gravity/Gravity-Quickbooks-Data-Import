using System.Text.Json.Serialization;

namespace QbAuthServr.Models;

public sealed class TokenResponse
{
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "";
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("x_refresh_token_expires_in")] public int XRefreshTokenExpiresIn { get; set; }
    [JsonPropertyName("id_token")] public string IdToken { get; set; } = "";
}
