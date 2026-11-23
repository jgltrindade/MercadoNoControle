using System.Globalization;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Reports;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.PriceHistory;
using Microsoft.EntityFrameworkCore;
using T = MercadoNoControle.Api.DTOs.Reports.ReportColumnType;

namespace MercadoNoControle.Api.Services.Analytics;

public interface IReportService
{
    Task<ReportTable> BuildAsync(string report, ReportQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Relatórios (especificação 4.7). Todos partem de <see cref="AnalyticsData"/> (compras confirmadas,
/// RN09) e produzem uma <see cref="ReportTable"/> usada tanto na tela quanto nas exportações.
/// </summary>
public sealed class ReportService(AnalyticsData data, AppDbContext db, TimeProvider timeProvider) : IReportService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["monthly"] = "Gastos mensais e anuais",
        ["stores"] = "Gastos por supermercado",
        ["categories"] = "Gastos por categoria",
        ["products"] = "Produtos",
        ["history"] = "Histórico de compras",
        ["comparison"] = "Comparação entre períodos"
    };

    public async Task<ReportTable> BuildAsync(string report, ReportQuery query, CancellationToken cancellationToken) => report switch
    {
        "monthly" => await MonthlyAsync(query, cancellationToken),
        "stores" => await StoresAsync(query, cancellationToken),
        "categories" => await CategoriesAsync(query, cancellationToken),
        "products" => await ProductsAsync(query, cancellationToken),
        "history" => await HistoryAsync(query, cancellationToken),
        "comparison" => await ComparisonAsync(query, cancellationToken),
        _ => throw new NotFoundException($"Relatório \"{report}\" não existe.")
    };

    private async Task<ReportTable> MonthlyAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        var months = AnalyticsData.Monthly(purchases, period);

        var rows = new List<ReportRow>();
        foreach (var year in months.GroupBy(m => m.Year))
        {
            foreach (var month in year)
            {
                rows.Add(new ReportRow([MonthLabel(month.Year, month.Month), month.PurchaseCount, month.Total,
                    AnalyticsData.AverageTicket(month.Total, month.PurchaseCount)]));
            }

            var yearTotal = year.Sum(m => m.Total);
            var yearCount = year.Sum(m => m.PurchaseCount);
            rows.Add(new ReportRow([$"Total {year.Key}", yearCount, yearTotal, AnalyticsData.AverageTicket(yearTotal, yearCount)], IsTotal: true));
        }

        if (months.Select(m => m.Year).Distinct().Count() > 1)
        {
            var total = months.Sum(m => m.Total);
            var count = months.Sum(m => m.PurchaseCount);
            rows.Add(new ReportRow(["Total do período", count, total, AnalyticsData.AverageTicket(total, count)], IsTotal: true));
        }

        return await TableAsync("monthly", query, period, null,
            [Column("month", "Mês", T.Text), Column("purchases", "Compras", T.Integer), Column("total", SpentHeader(query), T.Money), Column("ticket", "Ticket médio", T.Money)],
            rows, cancellationToken);
    }

    private async Task<ReportTable> StoresAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var stores = AnalyticsData.ByStore(await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken));

        var rows = stores
            .Select(s => new ReportRow([s.StoreName, s.PurchaseCount, s.Total, AnalyticsData.AverageTicket(s.Total, s.PurchaseCount), s.SharePercent]))
            .ToList();
        var total = stores.Sum(s => s.Total);
        var count = stores.Sum(s => s.PurchaseCount);
        rows.Add(new ReportRow(["Total", count, total, AnalyticsData.AverageTicket(total, count), total == 0 ? 0m : 100m], IsTotal: true));

        return await TableAsync("stores", query, period, null,
            [Column("store", "Supermercado", T.Text), Column("purchases", "Compras", T.Integer), Column("total", SpentHeader(query), T.Money),
             Column("ticket", "Ticket médio", T.Money), Column("share", "Participação", T.Percent)],
            rows, cancellationToken);
    }

    private async Task<ReportTable> CategoriesAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        var categories = AnalyticsData.ByCategory(purchases, includeAdjustments: query.CategoryId is null);

        var rows = categories.Select(c => new ReportRow([c.CategoryName, c.Total, c.SharePercent])).ToList();
        var total = categories.Sum(c => c.Total);
        rows.Add(new ReportRow(["Total", total, total == 0 ? 0m : 100m], IsTotal: true));

        var notes = query.CategoryId is null
            ? "Valores dos itens por categoria. \"Ajustes da nota\" reúne descontos e acréscimos gerais, para que o total feche com o valor pago."
            : null;
        return await TableAsync("categories", query, period, notes,
            [Column("category", "Categoria", T.Text), Column("total", "Total", T.Money), Column("share", "Participação", T.Percent)],
            rows, cancellationToken);
    }

    private async Task<ReportTable> ProductsAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var ranking = query.Ranking ?? "spent";
        if (ranking == "increase")
        {
            return await PriceIncreaseAsync(query, period, cancellationToken);
        }

        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        var products = purchases
            .SelectMany(p => p.Items)
            .Where(i => i.ProductId is not null)
            .GroupBy(i => new { ProductId = i.ProductId!.Value, i.ProductName, i.CategoryName })
            .Select(g => new
            {
                g.Key.ProductName,
                Category = g.Key.CategoryName ?? AnalyticsData.WithoutCategory,
                Count = g.Count(),
                Quantity = g.Sum(i => i.Quantity),
                Unit = g.GroupBy(i => i.Unit ?? "").OrderByDescending(u => u.Count()).First().Key,
                Total = g.Sum(i => i.Total)
            });

        var ordered = ranking == "purchases"
            ? products.OrderByDescending(p => p.Count).ThenByDescending(p => p.Total)
            : products.OrderByDescending(p => p.Total).ThenByDescending(p => p.Count);

        var rows = ordered
            .ThenBy(p => p.ProductName)
            .Take(query.Top)
            .Select(p => new ReportRow([p.ProductName, p.Category, p.Count, p.Quantity, p.Unit, p.Total]))
            .ToList();

        var title = ranking == "purchases" ? "Produtos mais comprados" : "Produtos com maior gasto acumulado";
        return await TableAsync("products", query, period, "Considera apenas itens associados a produtos.",
            [Column("product", "Produto", T.Text), Column("category", "Categoria", T.Text), Column("purchases", "Compras", T.Integer),
             Column("quantity", "Quantidade", T.Number), Column("unit", "Unidade", T.Text), Column("total", "Total gasto", T.Money)],
            rows, cancellationToken, title);
    }

    /// <summary>Variação entre o primeiro e o último preço pago (total ÷ quantidade) no período.</summary>
    private async Task<ReportTable> PriceIncreaseAsync(ReportQuery query, AnalyticsPeriod period, CancellationToken cancellationToken)
    {
        var observations = db.PriceObservations.AsNoTracking()
            .Where(o => o.ObservedAt >= period.Start && o.ObservedAt < period.EndExclusive);
        if (query.StoreId is { } storeId)
        {
            observations = observations.Where(o => o.StoreId == storeId);
        }

        if (query.CategoryId is { } categoryId)
        {
            observations = observations.Where(o => o.Product.CategoryId == categoryId);
        }

        var rows = await observations
            .Select(o => new
            {
                o.Id,
                o.ProductId,
                o.Product.Name,
                Category = o.Product.Category != null ? o.Product.Category.Name : null,
                o.ObservedAt,
                o.TotalPrice,
                o.Quantity
            })
            .ToListAsync(cancellationToken);

        var increases = rows
            .GroupBy(r => r.ProductId)
            .Where(g => g.Count() >= 2)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.ObservedAt).ThenBy(r => r.Id).ToList();
                var first = ordered[0];
                var last = ordered[^1];
                var firstPrice = PriceNormalizer.EffectiveUnitPrice(first.TotalPrice, first.Quantity);
                var lastPrice = PriceNormalizer.EffectiveUnitPrice(last.TotalPrice, last.Quantity);
                return new
                {
                    first.Name,
                    Category = first.Category ?? AnalyticsData.WithoutCategory,
                    FirstDate = first.ObservedAt,
                    FirstPrice = firstPrice,
                    LastDate = last.ObservedAt,
                    LastPrice = lastPrice,
                    Variation = AnalyticsData.Variation(firstPrice, lastPrice)
                };
            })
            .Where(p => p.Variation is not null)
            .OrderByDescending(p => p.Variation)
            .ThenBy(p => p.Name)
            .Take(query.Top)
            .Select(p => new ReportRow([p.Name, p.Category, p.FirstDate, p.FirstPrice, p.LastDate, p.LastPrice, p.Variation]))
            .ToList();

        return await TableAsync("products", query, period,
            "Preço pago por unidade (total do item ÷ quantidade) na primeira e na última compra do período; produtos com ao menos duas compras.",
            [Column("product", "Produto", T.Text), Column("category", "Categoria", T.Text), Column("firstDate", "Primeira compra", T.Date),
             Column("firstPrice", "Preço inicial", T.Money), Column("lastDate", "Última compra", T.Date), Column("lastPrice", "Último preço", T.Money),
             Column("variation", "Variação", T.Percent)],
            increases, cancellationToken, "Produtos com maior aumento de preço");
    }

    private async Task<ReportTable> HistoryAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        var ids = purchases.Select(p => p.ReceiptId).ToList();
        var numbers = await db.Receipts.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Number })
            .ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);

        var rows = purchases
            .OrderByDescending(p => p.IssuedAt)
            .Select(p => new ReportRow([p.IssuedAt, p.StoreName, numbers.GetValueOrDefault(p.ReceiptId), p.Items.Count, p.Spent]))
            .ToList();
        rows.Add(new ReportRow(["Total", $"{purchases.Count} compra(s)", null, purchases.Sum(p => p.Items.Count), purchases.Sum(p => p.Spent)], IsTotal: true));

        return await TableAsync("history", query, period, null,
            [Column("date", "Data", T.Date), Column("store", "Supermercado", T.Text), Column("number", "Número", T.Text),
             Column("items", "Itens", T.Integer), Column("total", SpentHeader(query), T.Money)],
            rows, cancellationToken);
    }

    private async Task<ReportTable> ComparisonAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var current = data.ResolvePeriod(query);
        AnalyticsPeriod previous;
        if (query.CompareFrom is { } compareFrom && query.CompareTo is { } compareTo)
        {
            previous = data.ResolvePeriod(new ReportQuery { From = compareFrom, To = compareTo });
        }
        else
        {
            var days = current.To.DayNumber - current.From.DayNumber;
            var end = current.From.AddDays(-1);
            previous = new AnalyticsPeriod(end.AddDays(-days), end);
        }

        var a = await data.LoadPurchasesAsync(previous, query.StoreId, query.CategoryId, cancellationToken);
        var b = await data.LoadPurchasesAsync(current, query.StoreId, query.CategoryId, cancellationToken);
        var categoriesA = AnalyticsData.ByCategory(a, query.CategoryId is null).ToDictionary(c => c.CategoryName, c => c.Total);
        var categoriesB = AnalyticsData.ByCategory(b, query.CategoryId is null).ToDictionary(c => c.CategoryName, c => c.Total);

        var rows = categoriesA.Keys.Union(categoriesB.Keys)
            .Select(name => (Name: name, A: categoriesA.GetValueOrDefault(name), B: categoriesB.GetValueOrDefault(name)))
            .OrderByDescending(c => c.B).ThenByDescending(c => c.A).ThenBy(c => c.Name)
            .Select(c => new ReportRow([c.Name, c.A, c.B, c.B - c.A, AnalyticsData.Variation(c.A, c.B)]))
            .ToList();
        var totalA = a.Sum(p => p.Spent);
        var totalB = b.Sum(p => p.Spent);
        rows.Add(new ReportRow(["Total", totalA, totalB, totalB - totalA, AnalyticsData.Variation(totalA, totalB)], IsTotal: true));

        var notes = $"Período anterior: {Format(previous)} — {a.Count} compra(s), ticket médio {Money(AnalyticsData.AverageTicket(totalA, a.Count))}. " +
                    $"Período atual: {Format(current)} — {b.Count} compra(s), ticket médio {Money(AnalyticsData.AverageTicket(totalB, b.Count))}.";
        return await TableAsync("comparison", query, current, notes,
            [Column("category", "Categoria", T.Text), Column("previous", "Período anterior", T.Money), Column("current", "Período atual", T.Money),
             Column("difference", "Diferença", T.Money), Column("variation", "Variação", T.Percent)],
            rows, cancellationToken);
    }

    private async Task<ReportTable> TableAsync(
        string key, ReportQuery query, AnalyticsPeriod period, string? notes, IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<ReportRow> rows, CancellationToken cancellationToken, string? title = null)
    {
        var store = await data.StoreNameAsync(query.StoreId, cancellationToken);
        var category = await data.CategoryNameAsync(query.CategoryId, cancellationToken);
        var filters = $"Período: {Format(period)} · Supermercado: {store ?? "todos"} · Categoria: {category ?? "todas"}";
        return new ReportTable(key, title ?? Titles[key], filters, notes, timeProvider.GetUtcNow().UtcDateTime, columns, rows);
    }

    private static ReportColumn Column(string key, string header, ReportColumnType type) => new(key, header, type);

    private static string SpentHeader(ReportQuery query) => query.CategoryId is null ? "Total" : "Total na categoria";

    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMM/yyyy", PtBr).Replace(".", string.Empty);

    private static string Format(AnalyticsPeriod period) =>
        $"{period.From.ToString("dd/MM/yyyy", PtBr)} a {period.To.ToString("dd/MM/yyyy", PtBr)}";

    private static string Money(decimal? value) => value is { } v ? v.ToString("C", PtBr) : "—";
}
