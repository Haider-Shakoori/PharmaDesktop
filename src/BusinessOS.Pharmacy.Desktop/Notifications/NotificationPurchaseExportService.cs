using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed record StockPurchaseAlert(
    string Medicine,
    string Status,
    decimal CurrentStock,
    decimal MinimumStock,
    decimal SuggestedPurchase);

public sealed class NotificationPurchaseExportService
{
    public string? ExportToExcel(IReadOnlyList<StockPurchaseAlert> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export stock purchase list to Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"Darmaltoon-Purchase-List-{DateTime.Now:yyyy-MM-dd}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        WriteXlsx(dialog.FileName, items);
        return dialog.FileName;
    }

    public bool Print(IReadOnlyList<StockPurchaseAlert> items)
    {
        if (items.Count == 0)
        {
            return false;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var document = BuildPrintDocument(items);
        document.PageHeight = dialog.PrintableAreaHeight;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PagePadding = new Thickness(42);
        document.ColumnWidth = double.PositiveInfinity;

        dialog.PrintDocument(
            ((IDocumentPaginatorSource)document).DocumentPaginator,
            "Darmaltoon stock purchase list");

        return true;
    }

    private static FlowDocument BuildPrintDocument(
        IReadOnlyList<StockPurchaseAlert> items)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
        };

        document.Blocks.Add(new Paragraph(new Run("Darmaltoon — Stock Purchase List"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        document.Blocks.Add(new Paragraph(
            new Run($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} · {items.Count:N0} item(s)"))
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var table = new Table { CellSpacing = 0 };
        var widths = new[] { 34d, 210d, 90d, 82d, 82d, 105d };
        foreach (var width in widths)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(width) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header = new TableRow();
        AddCell(header, "#", true);
        AddCell(header, "Medicine", true);
        AddCell(header, "Status", true);
        AddCell(header, "Current", true);
        AddCell(header, "Minimum", true);
        AddCell(header, "Suggested purchase", true);
        group.Rows.Add(header);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var row = new TableRow();
            AddCell(row, (index + 1).ToString(CultureInfo.InvariantCulture));
            AddCell(row, item.Medicine);
            AddCell(row, item.Status);
            AddCell(row, item.CurrentStock.ToString("N2", CultureInfo.InvariantCulture));
            AddCell(row, item.MinimumStock.ToString("N2", CultureInfo.InvariantCulture));
            AddCell(row, item.SuggestedPurchase.ToString("N2", CultureInfo.InvariantCulture));
            group.Rows.Add(row);
        }

        document.Blocks.Add(table);
        return document;
    }

    private static void AddCell(
        TableRow row,
        string text,
        bool header = false)
    {
        row.Cells.Add(new TableCell(new Paragraph(new Run(text))
        {
            Margin = new Thickness(5, 4, 5, 4),
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
        })
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(2),
            Background = header ? Brushes.WhiteSmoke : Brushes.White,
        });
    }

    private static void WriteXlsx(
        string path,
        IReadOnlyList<StockPurchaseAlert> items)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(
            archive,
            "[Content_Types].xml",
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");

        WriteEntry(
            archive,
            "_rels/.rels",
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

        WriteEntry(
            archive,
            "xl/workbook.xml",
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets><sheet name=""Purchase List"" sheetId=""1"" r:id=""rId1""/></sheets>
</workbook>");

        WriteEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");

        WriteEntry(
            archive,
            "xl/styles.xml",
            @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts>
  <fills count=""2""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill></fills>
  <borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
  <cellXfs count=""2""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0""/></cellXfs>
</styleSheet>");

        var sheet = archive.CreateEntry(
            "xl/worksheets/sheet1.xml",
            CompressionLevel.Optimal);

        using var stream = sheet.Open();
        using var writer = XmlWriter.Create(
            stream,
            new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
            });

        writer.WriteStartDocument();
        writer.WriteStartElement(
            "worksheet",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        writer.WriteStartElement("cols");
        WriteColumn(writer, 1, 1, 6);
        WriteColumn(writer, 2, 2, 34);
        WriteColumn(writer, 3, 3, 18);
        WriteColumn(writer, 4, 6, 18);
        writer.WriteEndElement();

        writer.WriteStartElement("sheetData");

        WriteRow(
            writer,
            1,
            ["#", "Medicine", "Status", "Current Stock", "Minimum Stock", "Suggested Purchase"],
            header: true);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            WriteRow(
                writer,
                index + 2,
                [
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    item.Medicine,
                    item.Status,
                    item.CurrentStock.ToString("0.##", CultureInfo.InvariantCulture),
                    item.MinimumStock.ToString("0.##", CultureInfo.InvariantCulture),
                    item.SuggestedPurchase.ToString("0.##", CultureInfo.InvariantCulture),
                ]);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteColumn(
        XmlWriter writer,
        int min,
        int max,
        double width)
    {
        writer.WriteStartElement("col");
        writer.WriteAttributeString("min", min.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("max", max.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("customWidth", "1");
        writer.WriteEndElement();
    }

    private static void WriteRow(
        XmlWriter writer,
        int rowNumber,
        IReadOnlyList<string> cells,
        bool header = false)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString(
            "r",
            rowNumber.ToString(CultureInfo.InvariantCulture));

        for (var index = 0; index < cells.Count; index++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString(
                "r",
                $"{ColumnName(index + 1)}{rowNumber}");
            writer.WriteAttributeString("t", "inlineStr");

            if (header)
            {
                writer.WriteAttributeString("s", "1");
            }

            writer.WriteStartElement("is");
            writer.WriteElementString("t", cells[index]);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;

        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(false));

        writer.Write(content);
    }
}
