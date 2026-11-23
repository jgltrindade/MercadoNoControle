using MercadoNoControle.Api.DTOs.Analytics;

namespace MercadoNoControle.Api.Services.Analytics;

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(AnalyticsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonthlyTotal>> GetMonthlyAsync(AnalyticsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoreTotal>> GetStoresAsync(AnalyticsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryTotal>> GetCategoriesAsync(AnalyticsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductTotal>> GetProductsAsync(AnalyticsQuery query, int top, CancellationToken cancellationToken);
}

/// <summary>Indicadores e distribuições do dashboard (especificação 4.1). Somente compras confirmadas.</summary>
public sealed class DashboardService(AnalyticsData data) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(AnalyticsQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);

        var total = purchases.Sum(p => p.Spent);
        var largest = purchases.OrderByDescending(p => p.Spent).ThenByDescending(p => p.IssuedAt).FirstOrDefault();
        var topStore = AnalyticsData.ByStore(purchases).FirstOrDefault();

        var today = data.Today;
        var month = new AnalyticsPeriod(new DateOnly(today.Year, today.Month, 1), today);
        var year = new AnalyticsPeriod(new DateOnly(today.Year, 1, 1), today);
        var monthTotal = (await data.LoadPurchasesAsync(month, query.StoreId, query.CategoryId, cancellationToken)).Sum(p => p.Spent);
        var yearTotal = (await data.LoadPurchasesAsync(year, query.StoreId, query.CategoryId, cancellationToken)).Sum(p => p.Spent);

        return new DashboardSummary(
            period.From,
            period.To,
            total,
            purchases.Count,
            AnalyticsData.AverageTicket(total, purchases.Count),
            largest is null ? null : new PurchaseReference(largest.ReceiptId, largest.Spent, largest.IssuedAt, largest.StoreName),
            topStore is null ? null : new StoreReference(topStore.StoreId, topStore.StoreName, topStore.Total),
            monthTotal,
            yearTotal);
    }

    public async Task<IReadOnlyList<MonthlyTotal>> GetMonthlyAsync(AnalyticsQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        return AnalyticsData.Monthly(await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken), period);
    }

    public async Task<IReadOnlyList<StoreTotal>> GetStoresAsync(AnalyticsQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        return AnalyticsData.ByStore(await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken));
    }

    public async Task<IReadOnlyList<CategoryTotal>> GetCategoriesAsync(AnalyticsQuery query, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        return AnalyticsData.ByCategory(purchases, includeAdjustments: query.CategoryId is null);
    }

    public async Task<IReadOnlyList<ProductTotal>> GetProductsAsync(AnalyticsQuery query, int top, CancellationToken cancellationToken)
    {
        var period = data.ResolvePeriod(query);
        var purchases = await data.LoadPurchasesAsync(period, query.StoreId, query.CategoryId, cancellationToken);
        return AnalyticsData.ByProduct(purchases).Take(Math.Clamp(top, 1, 50)).ToList();
    }
}
