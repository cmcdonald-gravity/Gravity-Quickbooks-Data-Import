using ClosedXML.Excel;
using QbAuthServr.Models;

namespace QbAuthServr.Services.Excel;

public sealed class ChartOfAccountsExportService
{
    private static (string parent, string isSub, int level) DeriveHierarchy(string fq)
    {
        if (string.IsNullOrWhiteSpace(fq) || !fq.Contains(':'))
            return ("", "No", 0);

        var parts = fq.Split(':');
        var parent = string.Join(":", parts.Take(parts.Length - 1));
        var level = parts.Length - 1;
        return (parent, "Yes", level);
    }

    public (byte[] content, string fileName) BuildWorkbook(
        IReadOnlyList<AccountRow> accounts,
        string webRootPath,
        string templateFileName = "Active Chart of Accounts.xlsx")
    {
        var templatePath = Path.Combine(webRootPath ?? "", "templates", templateFileName);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template not found at {templatePath}");

        using var wb = new XLWorkbook(templatePath);
        var ws = wb.Worksheets.Worksheet(1); // "Active Chart of Accounts"

        // Locate header row by "Account"
        int headerRow = 1;
        bool found = false;
        for (int r = 1; r <= 10 && !found; r++)
        {
            for (int c = 1; c <= 200; c++)
            {
                if (string.Equals(ws.Cell(r, c).GetString(), "Account", StringComparison.OrdinalIgnoreCase))
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

        void Set(string header, int row, object? value)
        {
            if (!col.TryGetValue(header, out var c)) return;
            var cell = ws.Cell(row, c);
            if (value is null) return;

            switch (value)
            {
                case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                case decimal dec: cell.Value = (double)dec; break;
                case double d:    cell.Value = d; break;
                case float f:     cell.Value = (double)f; break;
                case int i:       cell.Value = i; break;
                case long l:      cell.Value = (double)l; break;
                case bool b:      cell.Value = b; break;
                default:          cell.Value = value.ToString(); break;
            }
        }

        foreach (var a in accounts)
        {
            var row = NextRow();

            var (parent, isSub, level) = DeriveHierarchy(a.FullyQualifiedName);
            var status = a.Active ? "Active" : "Inactive";

            // Minimal but useful mapping for Gravity template
            Set("Account", row, a.AcctNum);
            Set("Account Name", row, a.Name);
            Set("Account Full Name", row, a.FullyQualifiedName);
            Set("Account Number", row, a.AcctNum);
            Set("Account Type", row, a.AccountType);
            Set("Account Status", row, status);
            Set("Status", row, status);
            Set("Status Reason", row, status);
            Set("Reference ID", row, a.Id);

            Set("Sub Account", row, isSub);
            if (!string.IsNullOrWhiteSpace(parent)) Set("Parent Account", row, parent);
            if (level > 0) Set("Sub Account Level", row, level);

            // Leave everything else blank by design (safe defaults)
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        return (ms.ToArray(), $"ChartOfAccounts-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }
}