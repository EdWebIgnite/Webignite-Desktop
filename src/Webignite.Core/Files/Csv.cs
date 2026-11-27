using System.Text;

namespace Webignite.Core.Files;

/// <summary>Reading CSV files as Excel, Xero and QuickBooks save them: quoted cells, doubled quotes, commas or semicolons.</summary>
public static class Csv
{
    /// <summary>The rows of a CSV file, each a list of cells; empty rows are left out. Commas or semicolons, as the header line says.</summary>
    public static List<IReadOnlyList<string>> Read(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var separator = Separator(text);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    cell.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(v => v.Length > 0))
                    rows.Add(row);
                row = [];
            }
            else
                cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(v => v.Length > 0))
            rows.Add(row);
        return rows;
    }

    // Excel in South Africa often saves CSV with semicolons; the header line says which it is.
    private static char Separator(string text)
    {
        var header = text.Split('\n')[0];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }
}
