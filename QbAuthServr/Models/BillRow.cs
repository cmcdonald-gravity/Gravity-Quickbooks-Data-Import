namespace QbAuthServr.Models;

public sealed class BillRow
{
    public string Id { get; set; } = "";
    public string DocNumber { get; set; } = "";
    public string TxnDate { get; set; } = "";
    public decimal TotalAmt { get; set; }
    public string Memo { get; set; } = "";
    public string VendorName { get; set; } = "";
    public string VendorId { get; set; } = "";
}