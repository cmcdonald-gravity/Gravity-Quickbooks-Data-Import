namespace QbAuthServr.Options;

public sealed class QuickBooksOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Scopes { get; set; } = "com.intuit.quickbooks.accounting openid profile email";
}