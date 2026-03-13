using ClosedXML.Excel;
using QbAuthServr.Models;

namespace QbAuthServr.Services.Excel;

public sealed class VoucherExportService
{
    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, out var dt)) return dt;
        return null;
    }

    public (byte[] content, string fileName) BuildWorkbook(
        IReadOnlyList<BillRow> bills,
        string webRootPath,
        string templateFileName = "All Vouchers.xlsx")
    {
        var templatePath = Path.Combine(webRootPath ?? "", "templates", templateFileName);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template not found at {templatePath}");

        using var wb = new XLWorkbook(templatePath);
        var ws = wb.Worksheets.Worksheet(1); // "All Vouchers"

        // Find header row by "Transaction ID"
        int headerRow = 1;
        bool found = false;
        for (int r = 1; r <= 10 && !found; r++)
        {
            for (int c = 1; c <= 200; c++)
            {
                if (string.Equals(ws.Cell(r, c).GetString(), "Transaction ID", StringComparison.OrdinalIgnoreCase))
                { headerRow = r; found = true; break; }
            }
        }

        // Map header -> column
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= 500; c++)
        {
            var name = ws.Cell(headerRow, c).GetString();
            if (string.IsNullOrWhiteSpace(name)) break;
            col[name] = c;
        }

        int NextRow()
        {
            var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            return last + 1;
        }

        void Set(string header, int row, object? value, string? dateFmt = null)
        {
            if (!col.TryGetValue(header, out var c)) return;
            var cell = ws.Cell(row, c);
            if (value is null) return;

            switch (value)
            {
                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = string.IsNullOrWhiteSpace(dateFmt) ? "yyyy-mm-dd" : dateFmt;
                    break;
                case decimal dec: cell.Value = (double)dec; break;
                case double d:    cell.Value = d; break;
                case float f:     cell.Value = (double)f; break;
                case int i:       cell.Value = i; break;
                case long l:      cell.Value = (double)l; break;
                case bool b:      cell.Value = b; break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }

        foreach (var b in bills)
        {
            var row = NextRow();

            var txnId   = !string.IsNullOrWhiteSpace(b.DocNumber) ? $"AP-{b.DocNumber}" : $"AP-{b.Id}";
            var invDate = ParseDate(b.TxnDate) ?? DateTime.Today;

            // Minimal mapping (same as working solution)
            Set("Transaction ID", row, txnId);
            Set("Transaction Mode", row, "Bill");
            Set("Document Number", row, b.DocNumber);
            Set("Invoice Date", row, invDate);
            Set("Apply Date", row, invDate);
            Set("Vendor ID", row, b.VendorId);
            Set("Vendor Name", row, b.VendorName);
            Set("Invoice Amount", row, b.TotalAmt);
            Set("Remaining Amount", row, b.TotalAmt);
            Set("Posting Status", row, "Unposted");
            Set("Created On", row, DateTime.UtcNow);
            Set("Description", row, b.Memo);
            Set("Entry Date", row, DateTime.UtcNow);
            Set("Sub Total", row, b.TotalAmt);
            Set("Total", row, b.TotalAmt);
            Set("Total Tax", row, 0m);
            Set("Status", row, "Active");
            Set("Status Reason", row, "Unposted");
            Set("Voucher Note", row, b.Memo);
            Set("Reference ID", row, b.Id);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        return (ms.ToArray(), $"Vouchers-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }
}