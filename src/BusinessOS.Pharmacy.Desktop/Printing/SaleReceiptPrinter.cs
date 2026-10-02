using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;

namespace BusinessOS.Pharmacy.Desktop.Printing;

public interface ISaleReceiptPrinter
{
    bool Print(SaleDetail sale);
}

public sealed class SaleReceiptPrinter : ISaleReceiptPrinter
{
    private readonly ReceiptSettingsStore? _settingsStore;

    public SaleReceiptPrinter()
    {
    }

    public SaleReceiptPrinter(ReceiptSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    private ReceiptSettings CurrentSettings => _settingsStore?.Load() ?? new ReceiptSettings();

    public bool Print(SaleDetail sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var preview = new ReceiptPreviewWindow(sale, this)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        preview.ShowDialog();
        return preview.WasPrinted;
    }

    public IReadOnlyList<string> GetPrinterNames()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues()
            .Select(queue => queue.FullName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? GetDefaultPrinterName()
    {
        using var server = new LocalPrintServer();
        return server.DefaultPrintQueue?.FullName;
    }

    public void PrintToPrinter(SaleDetail sale, string printerName)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        using var server = new LocalPrintServer();
        using var queue = server.GetPrintQueue(printerName);
        var ticket = queue.DefaultPrintTicket;
        var capabilities = queue.GetPrintCapabilities(ticket);
        var pageWidth = capabilities.OrientedPageMediaWidth ?? 302d;
        var pageHeight = capabilities.OrientedPageMediaHeight ?? 1122d;
        var printableWidth = capabilities.PageImageableArea?.ExtentWidth ?? pageWidth;

        var document = CreateDocument(sale, Math.Clamp(printableWidth, 240d, 760d));
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, pageHeight);

        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(paginator, ticket);
    }

    public FlowDocument CreateDocument(SaleDetail sale, double pageWidth)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = Brushes.Black,
            PagePadding = new Thickness(14),
            // A finite, page-sized column keeps star-sized table columns measurable;
            // double.PositiveInfinity collapses them in WPF FlowDocument tables.
            ColumnWidth = pageWidth,
            PageWidth = pageWidth,
            PageHeight = double.NaN,
        };

        document.Blocks.Add(new Paragraph(new Run("Darmaltoon Pharmacy"))
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        });

        document.Blocks.Add(new Paragraph(new Run(sale.Sale.StockLocationName))
        {
            FontSize = 9.5,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        });

        document.Blocks.Add(new Paragraph(new Run(sale.Sale.SaleNumber))
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        });

        // Receipt tables use absolute widths: star-sized FlowDocument columns can
        // collapse or overflow depending on the viewer, which mangles narrow receipts.
        var contentWidth = Math.Max(180d, pageWidth - document.PagePadding.Left - document.PagePadding.Right);
        const double qtyWidth = 48d;
        const double totalWidth = 72d;
        var itemWidth = Math.Max(80d, contentWidth - qtyWidth - totalWidth);
        var settings = CurrentSettings;

        document.Blocks.Add(BuildInfoTable(sale, contentWidth, settings));

        var lines = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 8),
        };
        lines.Columns.Add(new TableColumn { Width = new GridLength(itemWidth) });
        lines.Columns.Add(new TableColumn { Width = new GridLength(qtyWidth) });
        lines.Columns.Add(new TableColumn { Width = new GridLength(totalWidth) });

        var header = new TableRowGroup();
        var headerRow = new TableRow();
        headerRow.Cells.Add(Cell("Item", true));
        headerRow.Cells.Add(Cell("Qty", true, TextAlignment.Right));
        headerRow.Cells.Add(Cell("Total", true, TextAlignment.Right));
        header.Rows.Add(headerRow);
        lines.RowGroups.Add(header);

        var body = new TableRowGroup();
        foreach (var line in sale.Lines)
        {
            var row = new TableRow();
            row.Cells.Add(Cell(
                string.IsNullOrWhiteSpace(line.SaleUnit)
                    ? line.Description
                    : $"{line.Description}\n{line.SaleUnit}",
                false));
            row.Cells.Add(Cell($"{line.Quantity:0.####}", false, TextAlignment.Right));
            row.Cells.Add(Cell($"{line.LineTotal:N2}", false, TextAlignment.Right));
            body.Rows.Add(row);

            if (!settings.ShowBatchDetails)
            {
                continue;
            }

            foreach (var allocation in line.Allocations)
            {
                var allocationRow = new TableRow();
                allocationRow.Cells.Add(new TableCell(new Paragraph(new Run(
                    $"Batch {allocation.BatchNumber ?? "Unbatched"} · {allocation.Quantity:0.####} × {allocation.UnitPrice:N2}" +
                    (allocation.ExpiresAt is null ? string.Empty : $" · exp {allocation.ExpiresAt:yyyy-MM-dd}")))
                {
                    FontSize = 8.5,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(4, 0, 0, 3),
                })
                {
                    ColumnSpan = 3,
                    Padding = new Thickness(1),
                });
                body.Rows.Add(allocationRow);
            }
        }

        lines.RowGroups.Add(body);
        document.Blocks.Add(lines);

        var totals = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 8),
        };
        totals.Columns.Add(new TableColumn { Width = new GridLength(Math.Max(80d, contentWidth - 110d)) });
        totals.Columns.Add(new TableColumn { Width = new GridLength(110d) });
        var totalsRows = new TableRowGroup();
        totalsRows.Rows.Add(TotalRow("Subtotal", sale.Subtotal, false));
        totalsRows.Rows.Add(TotalRow("Discount", sale.DiscountTotal, false));
        totalsRows.Rows.Add(TotalRow("Total", sale.Sale.GrandTotal, true));
        totalsRows.Rows.Add(TotalRow("Paid", sale.Sale.PaidTotal, false));
        if (sale.Sale.DueTotal > 0m)
        {
            totalsRows.Rows.Add(TotalRow("Credit due", sale.Sale.DueTotal, true));
        }
        if (sale.Sale.ChangeTotal > 0m)
        {
            totalsRows.Rows.Add(TotalRow("Change", sale.Sale.ChangeTotal, false));
        }
        totals.RowGroups.Add(totalsRows);
        document.Blocks.Add(totals);

        if (settings.ShowPayments && sale.Payments.Count > 0)
        {
            document.Blocks.Add(new Paragraph(new Run("Payments"))
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 3, 0, 3),
            });

            foreach (var payment in sale.Payments)
            {
                var reference = string.IsNullOrWhiteSpace(payment.Reference)
                    ? string.Empty
                    : $" · {payment.Reference}";
                document.Blocks.Add(new Paragraph(new Run(
                    $"{payment.Method.ToUpperInvariant()} · {payment.Amount:N2} {payment.Currency}{reference}"))
                {
                    FontSize = 9,
                    Margin = new Thickness(0, 0, 0, 2),
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(sale.PrescriptionReference))
        {
            document.Blocks.Add(new Paragraph(new Run(
                $"Prescription: {sale.PrescriptionReference}" +
                (string.IsNullOrWhiteSpace(sale.PrescriberName) ? string.Empty : $" · {sale.PrescriberName}") +
                (sale.PrescriptionDate is null ? string.Empty : $" · {sale.PrescriptionDate:yyyy-MM-dd}")))
            {
                FontSize = 8.8,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        if (settings.ShowFooter && !string.IsNullOrWhiteSpace(settings.FooterText))
        {
            document.Blocks.Add(new Paragraph(new Run(settings.FooterText.Trim()))
            {
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        return document;
    }

    private static Table BuildInfoTable(SaleDetail sale, double contentWidth, ReceiptSettings settings)
    {
        var half = Math.Max(80d, contentWidth / 2d);
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(half) });
        table.Columns.Add(new TableColumn { Width = new GridLength(half) });

        var time = sale.Sale.CompletedAt is null
            ? string.Empty
            : sale.Sale.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        var rows = new TableRowGroup();
        rows.Rows.Add(InfoRow(
            $"Date: {sale.Sale.BusinessDate:yyyy-MM-dd}",
            settings.ShowCashier ? $"Cashier: {sale.CashierName}" : time));

        if (settings.ShowCustomer)
        {
            rows.Rows.Add(InfoRow(
                $"Customer: {sale.Sale.CustomerName ?? "Walk-in"}",
                settings.ShowCashier ? time : string.Empty));
        }
        else if (settings.ShowCashier && !string.IsNullOrWhiteSpace(time))
        {
            rows.Rows.Add(InfoRow(string.Empty, time));
        }

        table.RowGroups.Add(rows);
        return table;
    }

    private static TableRow InfoRow(string left, string right)
    {
        var row = new TableRow();
        row.Cells.Add(Cell(left, false));
        row.Cells.Add(Cell(right, false, TextAlignment.Right));
        return row;
    }

    private static TableRow TotalRow(string label, decimal amount, bool bold)
    {
        var row = new TableRow();
        row.Cells.Add(Cell(label, bold));
        row.Cells.Add(Cell($"AFN {amount:N2}", bold, TextAlignment.Right));
        return row;
    }

    private static TableCell Cell(
        string text,
        bool bold,
        TextAlignment alignment = TextAlignment.Left)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            TextAlignment = alignment,
        };

        if (bold)
        {
            paragraph.FontWeight = FontWeights.Bold;
        }

        return new TableCell(paragraph)
        {
            Padding = new Thickness(2, 3, 2, 3),
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(0, 0, 0, 0.5),
        };
    }
}
