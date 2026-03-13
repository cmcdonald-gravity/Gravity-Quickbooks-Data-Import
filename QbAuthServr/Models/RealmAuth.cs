namespace QbAuthServr.Models;

public sealed class RealmAuth
{
    public TokenResponse Tokens { get; set; } = new();
    public string? ApiHost { get; set; } // not used now (we force prod), kept for future
}