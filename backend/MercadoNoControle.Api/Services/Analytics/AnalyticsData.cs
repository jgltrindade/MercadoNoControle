using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Analytics;

/// <summary>Item de uma compra para análise.</summary>
public sealed record AnalyticsItem(
    int? ProductId,
    string? ProductName,
    int? CategoryId,
    string? CategoryName,
    decimal Quantity,
    string? Unit,
    decimal Total);

/// <summary>
/// Compra confirmada para análise. <see cref="Spent"/> é o valor considerado: o total da nota,
/// ou — com filtro de categoria — a soma dos itens da categoria.
/// </summary>
public sealed record AnalyticsPurchase(
    int ReceiptId,
    DateTime IssuedAt,
    int StoreId,
    string StoreName,
    decimal ReceiptTotal,
    decimal Spent,
    IReadOnlyList<AnalyticsItem> Items);

/// <summary>Período resolvido: início inclusivo e fim exclusivo, em datas locais da nota.</summary>
public sealed record AnalyticsPeriod(DateOnly From, DateOnly To)
{
    public DateTime Start => From.ToDateTime(TimeOnly.MinValue);

    public DateTime EndExclusive => To.AddDays(1).ToDateTime(TimeOnly.MinValue);
}

/// <summary>
/// Leitura das compras confirmadas (RN09) usada por dashboard e relatórios, para que ambos
/// partam exatamente dos mesmos dados. Somas e médias são feitas em decimal.
/// </summary>
public sealed class AnalyticsData(AppDbContext db, TimeProvider timeProvider)
{
    public const string WithoutCategory = "Sem categoria";
    public const string WithoutProduct = "Itens sem produto associado";
    public const string ReceiptAdjustments = "Ajustes da nota (descontos e acréscimos gerais)";

    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>Período informado ou, na falta, os últimos 12 meses (do 1º dia do mês até hoje).</summary>
    public AnalyticsPeriod ResolvePeriod(AnalyticsQuery query)
    {
        var to = query.To ?? Today;
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1).AddMonths(-11);
        if (from > to)
        {
            throw new BusinessRuleException(
                "Período inválido.",
                new Dictionary<string, string[]> { ["to"] = ["A data final deve ser igual ou posterior à inicial."] });
        }

        return new AnalyticsPeriod(from, to);
    }

    public async Task<IReadOnlyList<AnalyticsPurchase>> LoadPurchasesAsync(
        AnalyticsPeriod period, int? storeId, int? categoryId, CancellationToken cancellationToken)
    {
        var receipts = db.Receipts.AsNoTracking()
            .Where(r => r.Status == ReceiptStatus.Confirmed && r.IssuedAt >= period.Start && r.IssuedAt < period.EndExclusive);
        if (storeId is { } store)
        {
            receipts = receipts.Where(r => r.StoreId == store);
        }

        if (categoryId is { } category)
        {
            receipts = receipts.Where(r => r.Items.Any(i => i.Product != null && i.Product.CategoryId == category));
        }

        var rows = await receipts
            .OrderBy(r => r.IssuedAt)
            .ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.IssuedAt,
                r.StoreId,
                StoreName = r.Store.TradeName,
                r.Total,
                Items = r.Items.Select(i => new AnalyticsItem(
                    i.ProductId,
                    i.Product != null ? i.Product.Name : null,
                    i.Product != null ? i.Product.CategoryId : null,
                    i.Product != null && i.Product.Category != null ? i.Product.Category.Name : null,
                    i.Quantity,
                    i.Unit,
                    i.Total)).ToList()
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var items = categoryId is { } category ? r.Items.Where(i => i.CategoryId == category).ToList() : r.Items;
            var spent = categoryId is null ? r.Total : items.Sum(i => i.Total);
            return new AnalyticsPurchase(r.Id, r.IssuedAt, r.StoreId, r.StoreName, r.Total, spent, items);
        }).ToList();
    }

    public async Task<string?> StoreNameAsync(int? storeId, CancellationToken cancellationToken) =>
        storeId is { } id ? await db.Stores.Where(s => s.Id == id).Select(s => s.TradeName).SingleOrDefaultAsync(cancellationToken) : null;

    public async Task<string?> CategoryNameAsync(int? categoryId, CancellationToken cancellationToken) =>
        categoryId is { } id ? await db.Categories.Where(c => c.Id == id).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken) : null;

    // ---- Agregações compartilhadas -------------------------------------------------------

    public static IReadOnlyList<MonthlyTotal> Monthly(IReadOnlyList<AnalyticsPurchase> purchases, AnalyticsPeriod period)
    {
        var byMonth = purchases
            .GroupBy(p => (p.IssuedAt.Year, p.IssuedAt.Month))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.Spent), Count: g.Count()));

        var result = new List<MonthlyTotal>();
        for (var month = new DateOnly(period.From.Year, period.From.Month, 1); month <= period.To; month = month.AddMonths(1))
        {
            var (total, count) = byMonth.GetValueOrDefault((month.Year, month.Month));
            result.Add(new MonthlyTotal(month.Year, month.Month, total, count));
        }

        return result;
    }

    public static IReadOnlyList<StoreTotal> ByStore(IReadOnlyList<AnalyticsPurchase> purchases)
    {
        var total = purchases.Sum(p => p.Spent);
        return purchases
            .GroupBy(p => new { p.StoreId, p.StoreName })
            .Select(g => new StoreTotal(g.Key.StoreId, g.Key.StoreName, g.Sum(p => p.Spent), g.Count(), Share(g.Sum(p => p.Spent), total)))
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.StoreName)
            .ToList();
    }

    /// <summary>
    /// Gasto por categoria a partir dos itens. Sem filtro de categoria, a diferença entre o total das
    /// notas e a soma dos itens (descontos/acréscimos gerais) aparece como "Ajustes da nota", para que
    /// a distribuição feche com o total gasto.
    /// </summary>
    public static IReadOnlyList<CategoryTotal> ByCategory(IReadOnlyList<AnalyticsPurchase> purchases, bool includeAdjustments)
    {
        var total = purchases.Sum(p => p.Spent);
        var groups = purchases
            .SelectMany(p => p.Items)
            .GroupBy(i => new { i.CategoryId, Name = i.ProductId is null ? WithoutProduct : i.CategoryName ?? WithoutCategory })
            .Select(g => new { g.Key.CategoryId, g.Key.Name, Total = g.Sum(i => i.Total) })
            .ToList();

        var result = groups
            .Select(g => new CategoryTotal(g.CategoryId, g.Name, g.Total, Share(g.Total, total)))
            .ToList();

        if (includeAdjustments)
        {
            var adjustments = total - groups.Sum(g => g.Total);
            if (adjustments != 0)
            {
                result.Add(new CategoryTotal(null, ReceiptAdjustments, adjustments, Share(adjustments, total)));
            }
        }

        return result.OrderByDescending(c => c.Total).ThenBy(c => c.CategoryName).ToList();
    }

    public static IReadOnlyList<ProductTotal> ByProduct(IReadOnlyList<AnalyticsPurchase> purchases)
    {
        var total = purchases.Sum(p => p.Spent);
        return purchases
            .SelectMany(p => p.Items)
            .Where(i => i.ProductId is not null)
            .GroupBy(i => new { ProductId = i.ProductId!.Value, i.ProductName, i.CategoryName })
            .Select(g => new ProductTotal(
                g.Key.ProductId, g.Key.ProductName!, g.Key.CategoryName, g.Sum(i => i.Total), g.Count(), Share(g.Sum(i => i.Total), total)))
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.ProductName)
            .ToList();
    }

    /// <summary>Participação percentual com 2 casas.</summary>
    public static decimal Share(decimal part, decimal total) =>
        total == 0 ? 0m : DecimalPrecision.Round(part / total * 100m, 2);

    public static decimal? AverageTicket(decimal total, int count) =>
        count == 0 ? null : DecimalPrecision.Round(total / count, DecimalPrecision.Money);

    public static decimal? Variation(decimal from, decimal to) =>
        from == 0 ? null : DecimalPrecision.Round((to - from) / from * 100m, 2);
}
