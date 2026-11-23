using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MercadoNoControle.Api.DTOs.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MercadoNoControle.Api.Services.Analytics;

public sealed record ExportedFile(byte[] Content, string ContentType, string FileName);

/// <summary>Converte uma <see cref="ReportTable"/> em CSV, Excel ou PDF, sem consultar dados novamente.</summary>
public static class ReportExporter
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static ExportedFile Export(ReportTable table, string format, DateOnly today)
    {
        var baseName = $"mercado-no-controle-{table.Key}-{today:yyyyMMdd}";
        return format switch
        {
            "csv" => new ExportedFile(ToCsv(table), "text/csv; charset=utf-8", $"{baseName}.csv"),
            "xlsx" => new ExportedFile(ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{baseName}.xlsx"),
            "pdf" => new ExportedFile(ToPdf(table), "application/pdf", $"{baseName}.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato não suportado.")
        };
    }

    /// <summary>CSV para Excel em português: separador ";", vírgula decimal, UTF-8 com BOM.</summary>
    public static byte[] ToCsv(ReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', table.Columns.Select(c => Quote(c.Header))));
        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(';', row.Cells.Select((cell, index) => Quote(FormatCsv(cell, table.Columns[index].Type)))));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static byte[] ToExcel(ReportTable table)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(table.Title));

        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = table.Filters;
        var headerRow = 4;
        if (table.Notes is not null)
        {
            sheet.Cell(3, 1).Value = table.Notes;
            sheet.Cell(3, 1).Style.Font.SetItalic();
            headerRow = 5;
        }

        for (var column = 0; column < table.Columns.Count; column++)
        {
            var cell = sheet.Cell(headerRow, column + 1);
            cell.Value = table.Columns[column].Header;
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8F0E0"));
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var cell = sheet.Cell(headerRow + 1 + r, c + 1);
                SetExcelValue(cell, row.Cells[c], table.Columns[c].Type);
                if (row.IsTotal)
                {
                    cell.Style.Font.SetBold();
                }
            }
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents(headerRow, headerRow + table.Rows.Count);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] ToPdf(ReportTable table)
    {
        var landscape = table.Columns.Count > 5;
        var generated = table.GeneratedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(text => text.FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().Text("Mercado no Controle").FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(table.Title).FontSize(16).Bold();
                    column.Item().Text(table.Filters).FontColor(Colors.Grey.Darken2);
                    if (table.Notes is not null)
                    {
                        column.Item().Text(table.Notes).Italic().FontColor(Colors.Grey.Darken2);
                    }

                    column.Item().PaddingBottom(8);
                });

                page.Content().Table(pdfTable =>
                {
                    pdfTable.ColumnsDefinition(columns =>
                    {
                        foreach (var column in table.Columns)
                        {
                            if (column.Type == ReportColumnType.Text)
                            {
                                columns.RelativeColumn(2);
                            }
                            else
                            {
                                columns.RelativeColumn();
                            }
                        }
                    });

                    pdfTable.Header(header =>
                    {
                        foreach (var column in table.Columns)
                        {
                            var cell = header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4).PaddingHorizontal(2);
                            var text = (IsNumeric(column.Type) ? cell.AlignRight() : cell).Text(column.Header).Bold();
                        }
                    });

                    foreach (var row in table.Rows)
                    {
                        for (var c = 0; c < table.Columns.Count; c++)
                        {
                            var type = table.Columns[c].Type;
                            var cell = pdfTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2);
                            var text = (IsNumeric(type) ? cell.AlignRight() : cell).Text(FormatDisplay(row.Cells[c], type));
                            if (row.IsTotal)
                            {
                                text.Bold();
                            }
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Gerado em {generated}").FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    /// <summary>Valor formatado para exibição (pt-BR).</summary>
    public static string FormatDisplay(object? value, ReportColumnType type) => value switch
    {
        null => "—",
        decimal d when type == ReportColumnType.Money => d.ToString("C", PtBr),
        decimal d when type == ReportColumnType.Percent => $"{d.ToString("N2", PtBr)}%",
        decimal d => d.ToString("0.####", PtBr),
        DateTime date => date.ToString("dd/MM/yyyy", PtBr),
        IFormattable formattable => formattable.ToString(null, PtBr),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatCsv(object? value, ReportColumnType type) => value switch
    {
        null => string.Empty,
        decimal d when type is ReportColumnType.Money or ReportColumnType.Percent => d.ToString("0.00", PtBr),
        decimal d => d.ToString("0.####", PtBr),
        DateTime date => date.ToString("dd/MM/yyyy", PtBr),
        IFormattable formattable => formattable.ToString(null, PtBr),
        _ => value.ToString() ?? string.Empty
    };

    private static void SetExcelValue(IXLCell cell, object? value, ReportColumnType type)
    {
        switch (value)
        {
            case null:
                cell.Value = Blank.Value;
                break;
            case decimal d:
                cell.Value = type == ReportColumnType.Percent ? d / 100m : d;
                cell.Style.NumberFormat.Format = type switch
                {
                    ReportColumnType.Money => "\"R$\" #,##0.00",
                    ReportColumnType.Percent => "0.00%",
                    _ => "#,##0.####"
                };
                break;
            case int i:
                cell.Value = i;
                break;
            case DateTime date:
                cell.Value = date.Date;
                cell.Style.DateFormat.Format = "dd/MM/yyyy";
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    private static bool IsNumeric(ReportColumnType type) =>
        type is ReportColumnType.Integer or ReportColumnType.Number or ReportColumnType.Money or ReportColumnType.Percent;

    private static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string SheetName(string title)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var name = new string(title.Where(c => !invalid.Contains(c)).ToArray());
        return name.Length <= 31 ? name : name[..31];
    }
}
