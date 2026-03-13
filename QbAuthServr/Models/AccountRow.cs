namespace QbAuthServr.Models;

public sealed class AccountRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string AccountSubType { get; set; } = "";
    public bool Active { get; set; }
    public string AcctNum { get; set; } = "";
    public string FullyQualifiedName { get; set; } = "";
}