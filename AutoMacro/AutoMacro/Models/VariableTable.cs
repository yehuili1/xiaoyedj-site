using System.Data;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoMacro.Models;

/// <summary>
/// 变量表：简单的值列表，每行一个变量值，回放时按顺序逐行读取。
/// </summary>
public class VariableTable
{
    public DataTable Table { get; } = new("Variables");

    public VariableTable()
    {
        Table.Columns.Add("Value", typeof(string));
    }

    public string? GetValue(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Table.Rows.Count) return null;
        return Table.Rows[rowIndex]["Value"]?.ToString();
    }

    // Keep old signature for compatibility during transition
    public string? GetValue(string columnName, int rowIndex) => GetValue(rowIndex);

    public int RowCount => Table.Rows.Count;

    public List<string> GetAllValues()
    {
        return Table.Rows.Cast<DataRow>()
            .Select(r => r["Value"]?.ToString() ?? string.Empty)
            .ToList();
    }

    public void SetAllValues(List<string> values)
    {
        Table.Rows.Clear();
        foreach (var v in values)
        {
            var row = Table.NewRow();
            row["Value"] = v;
            Table.Rows.Add(row);
        }
    }

    public void AddRow(string value = "")
    {
        var row = Table.NewRow();
        row["Value"] = value;
        Table.Rows.Add(row);
    }

    public void RemoveRow(int index)
    {
        if (index >= 0 && index < Table.Rows.Count)
            Table.Rows.RemoveAt(index);
    }

    public void Clear()
    {
        Table.Rows.Clear();
    }

    public List<string> ColumnNames => new() { "Value" };

    public void AddColumn(string name) { }
    public void RemoveColumn(string name) { }

    public static VariableTable LoadFromCsv(string path)
    {
        var vt = new VariableTable();
        if (!File.Exists(path)) return vt;

        using var reader = new StreamReader(path);
        if (reader.Peek() == -1) return vt;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Mode = CsvMode.RFC4180,
            TrimOptions = TrimOptions.None,
            IgnoreBlankLines = false,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var value = csv.GetField(0) ?? string.Empty;
            vt.AddRow(value);
        }

        return vt;
    }

    public void SaveToCsv(string path)
    {
        using var writer = new StreamWriter(path, false);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Mode = CsvMode.RFC4180,
            NewLine = Environment.NewLine,
            ShouldQuote = args =>
            {
                var field = args.Field ?? string.Empty;
                return field.Contains(',') || field.Contains('"')
                    || field.Contains('\n') || field.Contains('\r');
            }
        };

        using var csv = new CsvWriter(writer, config);
        csv.WriteField("Value");
        csv.NextRecord();

        foreach (DataRow row in Table.Rows)
        {
            csv.WriteField(row["Value"]?.ToString() ?? string.Empty);
            csv.NextRecord();
        }
    }
}
