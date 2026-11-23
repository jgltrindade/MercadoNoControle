using System.Text;
using ClosedXML.Excel;
using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.DTOs.Reports;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Analytics;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Tests.Services;

/// <summary>
/// Cenário: 3 compras confirmadas e 1 rascunho.
/// R1 (A, 10/09): leite 10,00 + arroz 20,00 − desconto geral 2,00 = 28,00
/// R2 (B, 05/10): leite 12,00 + item sem produto 3,00 = 15,00
/// R3 (A, 15/11): arroz 25,00 = 25,00
/// Rascunho (B, 16/11): 100,00 — nunca entra (RN09).
/// </summary>
public class AnalyticsTests : IDisposable
{
    private static readonly AnalyticsQuery Autumn = new() { From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 11, 30) };

    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly int _storeA;
    private readonly int _storeB;
    private readonly int _r1;

    public AnalyticsTests()
    {
        using var context = _database.CreateContext();
        var a = TestData.Store("Mercado A", "11222333000181");
        var b = TestData.Store("Mercado B", "12345678000195");
        var milk = TestData.Product("Leite Integral 1L"); // Laticínios (4)
        var rice = new Product { Name = "Arroz 5kg", CategoryId = 1, Unit = MeasurementUnit.KG, PackageContent = 5m, CreatedAt = TestData.FixedNow };
        context.AddRange(a, b, milk, rice);

        var r1 = Receipt(a, new DateTime(2026, 9, 10, 10, 0, 0), 30.00m, 2.00m, 28.00m,
            Item(1, "LEITE", milk, 10.00m), Item(2, "ARROZ", rice, 20.00m));
        var r2 = Receipt(b, new DateTime(2026, 10, 5, 10, 0, 0), 15.00m, 0m, 15.00m,
            Item(1, "LEITE", milk, 12.00m), Item(2, "SEM PRODUTO", null, 3.00m));
        var r3 = Receipt(a, new DateTime(2026, 11, 15, 10, 0, 0), 25.00m, 0m, 25.00m, Item(1, "ARROZ", rice, 25.00m));
        var draft = Receipt(b, new DateTime(2026, 11, 16, 10, 0, 0), 100.00m, 0m, 100.00m, Item(1, "RASCUNHO", rice, 100.00m));
        draft.Status = ReceiptStatus.Draft;
        r1.Number = "111";
        context.Receipts.AddRange(r1, r2, r3, draft);
        context.SaveChanges();

        // Histórico de preços das compras confirmadas.
        var recorder = new PriceObservationRecorder(context);
        foreach (var receipt in context.Receipts.Include(r => r.Items).ThenInclude(i => i.PriceObservation).ToList())
        {
            recorder.SyncAsync(receipt, TestData.FixedNow, CancellationToken.None).GetAwaiter().GetResult();
        }

        context.SaveChanges();
        (_storeA, _storeB, _r1) = (a.Id, b.Id, r1.Id);
    }

    public void Dispose() => _database.Dispose();

    private static Receipt Receipt(Store store, DateTime date, decimal products, decimal discount, decimal total, params ReceiptItem[] items) => new()
    {
        Store = store,
        IssuedAt = date,
        ProductsTotal = products,
        DiscountTotal = discount,
        Total = total,
        Status = ReceiptStatus.Confirmed,
        CreatedAt = TestData.FixedNow,
        Items = items.ToList()
    };

    private static ReceiptItem Item(int line, string description, Product? product, decimal total) =>
        TestData.Item(line, description, 1m, total, total, product: product);

    private AnalyticsData Data() => new(_database.CreateContext(), new FixedTimeProvider());

    private DashboardService Dashboard() => new(Data());

    private ReportService Reports()
    {
        var context = _database.CreateContext();
        return new ReportService(new AnalyticsData(context, new FixedTimeProvider()), context, new FixedTimeProvider());
    }

    [Fact]
    public async Task Summary_uses_only_confirmed_purchases()
    {
        var summary = await Dashboard().GetSummaryAsync(Autumn, CancellationToken.None);

        Assert.Equal(68.00m, summary.TotalSpent);
        Assert.Equal(3, summary.PurchaseCount);
        Assert.Equal(22.67m, summary.AverageTicket);
        Assert.Equal(new PurchaseReference(_r1, 28.00m, new DateTime(2026, 9, 10, 10, 0, 0), "Mercado A"), summary.LargestPurchase);
        Assert.Equal(new StoreReference(_storeA, "Mercado A", 53.00m), summary.TopStore);
        Assert.Equal(25.00m, summary.CurrentMonthTotal); // novembro/2026 até "hoje" (20/11)
        Assert.Equal(68.00m, summary.CurrentYearTotal);
    }

    [Fact]
    public async Task Category_filter_sums_only_items_of_the_category()
    {
        var query = new AnalyticsQuery { From = Autumn.From, To = Autumn.To, CategoryId = 4 };

        var summary = await Dashboard().GetSummaryAsync(query, CancellationToken.None);
        var stores = await Dashboard().GetStoresAsync(query, CancellationToken.None);

        Assert.Equal(22.00m, summary.TotalSpent);
        Assert.Equal(2, summary.PurchaseCount);
        Assert.Equal([12.00m, 10.00m], stores.Select(s => s.Total));
        Assert.Equal(22.00m, summary.CurrentYearTotal);
    }

    [Fact]
    public async Task Store_filter_and_empty_periods()
    {
        var storeB = await Dashboard().GetSummaryAsync(new AnalyticsQuery { From = Autumn.From, To = Autumn.To, StoreId = _storeB }, CancellationToken.None);
        Assert.Equal(15.00m, storeB.TotalSpent);

        var empty = await Dashboard().GetSummaryAsync(new AnalyticsQuery { From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 1, 31) }, CancellationToken.None);
        Assert.Equal(0m, empty.TotalSpent);
        Assert.Null(empty.AverageTicket);
        Assert.Null(empty.LargestPurchase);
        Assert.Null(empty.TopStore);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Dashboard().GetSummaryAsync(
            new AnalyticsQuery { From = new DateOnly(2026, 12, 1), To = new DateOnly(2026, 11, 1) }, CancellationToken.None));
    }

    [Fact]
    public async Task Monthly_defaults_to_twelve_months_and_fills_gaps_with_zero()
    {
        var months = await Dashboard().GetMonthlyAsync(new AnalyticsQuery(), CancellationToken.None);

        Assert.Equal(12, months.Count);
        Assert.Equal((2025, 12), (months[0].Year, months[0].Month));
        Assert.Equal((2026, 11), (months[^1].Year, months[^1].Month));
        Assert.Equal(28.00m, months.Single(m => m.Month == 9).Total);
        Assert.Equal(15.00m, months.Single(m => m.Month == 10).Total);
        Assert.Equal(25.00m, months.Single(m => m.Month == 11).Total);
        Assert.Equal(0m, months.Single(m => m.Year == 2026 && m.Month == 1).Total);
        Assert.Equal(68.00m, months.Sum(m => m.Total));
    }

    [Fact]
    public async Task Category_distribution_reconciles_with_the_total_paid()
    {
        var categories = await Dashboard().GetCategoriesAsync(Autumn, CancellationToken.None);

        Assert.Equal(
            [("Mercearia", 45.00m), ("Laticínios", 22.00m), (AnalyticsData.WithoutProduct, 3.00m), (AnalyticsData.ReceiptAdjustments, -2.00m)],
            categories.Select(c => (c.CategoryName, c.Total)));
        Assert.Equal(68.00m, categories.Sum(c => c.Total));
        Assert.Equal(66.18m, categories[0].SharePercent);

        var products = await Dashboard().GetProductsAsync(Autumn, 10, CancellationToken.None);
        Assert.Equal([("Arroz 5kg", 45.00m, 2), ("Leite Integral 1L", 22.00m, 2)], products.Select(p => (p.ProductName, p.Total, p.PurchaseCount)));
    }

    [Fact]
    public async Task Monthly_report_has_year_subtotals_and_period_total()
    {
        var table = await Reports().BuildAsync("monthly", new ReportQuery(), CancellationToken.None);

        Assert.Equal(["Mês", "Compras", "Total", "Ticket médio"], table.Columns.Select(c => c.Header));
        var totals = table.Rows.Where(r => r.IsTotal).Select(r => (r.Cells[0], r.Cells[2])).ToList();
        Assert.Equal([("Total 2025", (object?)0m), ("Total 2026", 68.00m), ("Total do período", 68.00m)], totals);
        Assert.Contains(table.Rows, r => (string)r.Cells[0]! == "set/2026" && (decimal)r.Cells[2]! == 28.00m);
        Assert.Contains("Supermercado: todos", table.Filters);
    }

    [Fact]
    public async Task Store_category_and_history_reports_match_the_dashboard()
    {
        var query = new ReportQuery { From = Autumn.From, To = Autumn.To };

        var stores = await Reports().BuildAsync("stores", query, CancellationToken.None);
        Assert.Equal(["Mercado A", 2, 53.00m, 26.50m, 77.94m], stores.Rows[0].Cells);
        Assert.Equal(["Total", 3, 68.00m, 22.67m, 100m], stores.Rows[^1].Cells);

        var categories = await Reports().BuildAsync("categories", query, CancellationToken.None);
        Assert.Equal(68.00m, categories.Rows[^1].Cells[1]);
        Assert.NotNull(categories.Notes);

        var history = await Reports().BuildAsync("history", query, CancellationToken.None);
        Assert.Equal(4, history.Rows.Count);
        Assert.Equal(new DateTime(2026, 11, 15, 10, 0, 0), history.Rows[0].Cells[0]);
        Assert.Equal("111", history.Rows[2].Cells[2]);
        Assert.Equal(68.00m, history.Rows[^1].Cells[4]);
    }

    [Fact]
    public async Task Product_rankings()
    {
        var query = new ReportQuery { From = Autumn.From, To = Autumn.To };

        var spent = await Reports().BuildAsync("products", query, CancellationToken.None);
        Assert.Equal("Produtos com maior gasto acumulado", spent.Title);
        Assert.Equal(["Arroz 5kg", "Mercearia", 2, 2m, "UN", 45.00m], spent.Rows[0].Cells);

        query.Ranking = "increase";
        var increase = await Reports().BuildAsync("products", query, CancellationToken.None);
        Assert.Equal("Produtos com maior aumento de preço", increase.Title);
        Assert.Equal(["Arroz 5kg", "Leite Integral 1L"], increase.Rows.Select(r => r.Cells[0]));
        Assert.Equal(25.00m, increase.Rows[0].Cells[6]);   // arroz 20,00 → 25,00
        Assert.Equal(20.00m, increase.Rows[1].Cells[6]);   // leite 10,00 → 12,00
    }

    [Fact]
    public async Task Period_comparison_defaults_to_the_previous_period_of_same_length()
    {
        var table = await Reports().BuildAsync("comparison",
            new ReportQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 30) }, CancellationToken.None);

        var total = table.Rows[^1].Cells;
        Assert.Equal(["Total", 15.00m, 25.00m, 10.00m, 66.67m], total);
        Assert.Contains("02/10/2026 a 31/10/2026", table.Notes);
    }

    [Fact]
    public async Task Exports_reflect_the_report_table()
    {
        var table = await Reports().BuildAsync("stores", new ReportQuery { From = Autumn.From, To = Autumn.To }, CancellationToken.None);

        var csv = ReportExporter.Export(table, "csv", new DateOnly(2026, 11, 20));
        Assert.Equal("mercado-no-controle-stores-20261120.csv", csv.FileName);
        Assert.Equal(Encoding.UTF8.GetPreamble(), csv.Content[..3]);
        var lines = Encoding.UTF8.GetString(csv.Content[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Supermercado;Compras;Total;Ticket médio;Participação", lines[0]);
        Assert.Equal("Mercado A;2;53,00;26,50;77,94", lines[1]);
        Assert.Equal("Total;3;68,00;22,67;100,00", lines[^1]);

        var xlsx = ReportExporter.Export(table, "xlsx", new DateOnly(2026, 11, 20));
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx.Content)))
        {
            var sheet = workbook.Worksheet(1);
            Assert.Equal("Gastos por supermercado", sheet.Cell(1, 1).GetString());
            Assert.Equal("Supermercado", sheet.Cell(4, 1).GetString());
            Assert.Equal(53.00m, sheet.Cell(5, 3).GetValue<decimal>());
            Assert.Equal(0.7794m, sheet.Cell(5, 5).GetValue<decimal>());
            Assert.True(sheet.Cell(7, 1).Style.Font.Bold);
        }

        var pdf = ReportExporter.Export(table, "pdf", new DateOnly(2026, 11, 20));
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal("%PDF"u8.ToArray(), pdf.Content[..4]);
    }

    [Fact]
    public void Csv_quotes_values_with_separators()
    {
        var table = new ReportTable("history", "T", "F", null, TestData.FixedNow,
            [new ReportColumn("a", "Nome", ReportColumnType.Text), new ReportColumn("b", "Data", ReportColumnType.Date)],
            [new ReportRow(["Loja; \"Centro\"", new DateTime(2026, 11, 5)])]);

        var text = Encoding.UTF8.GetString(ReportExporter.ToCsv(table)[3..]);

        Assert.Contains("\"Loja; \"\"Centro\"\"\";05/11/2026", text);
    }
}
