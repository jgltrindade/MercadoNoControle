using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.PriceHistory;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Receipts;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.PriceHistory;

public interface IPriceHistoryService
{
    Task<ProductPriceHistoryResponse> GetProductPricesAsync(int productId, PriceHistoryQuery query, CancellationToken cancellationToken);

    Task<ProductPriceComparisonResponse> GetComparisonAsync(int productId, PriceHistoryQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnassignedItemGroup>> ListUnassignedAsync(CancellationToken cancellationToken);

    Task<AssignItemsResponse> AssignAsync(AssignItemsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Histórico e comparação de preços (especificação 4.5). Somente observações de compras
/// confirmadas existem (RN03, RN09). O histórico é sempre do próprio produto (RN07); a lista de
/// produtos da mesma categoria serve apenas para comparar o preço normalizado (RN08).
/// Médias e variações são calculadas em decimal no backend.
/// </summary>
public sealed class PriceHistoryService(
    AppDbContext db,
    PriceObservationRecorder priceRecorder,
    TimeProvider timeProvider) : IPriceHistoryService
{
    private const int AlternativesLimit = 10;
    private const int UnassignedLimit = 100;

    public async Task<ProductPriceHistoryResponse> GetProductPricesAsync(
        int productId, PriceHistoryQuery query, CancellationToken cancellationToken)
    {
        var product = await FindProductAsync(productId, cancellationToken);
        var points = await LoadPointsAsync(productId, query, cancellationToken);

        return new ProductPriceHistoryResponse(
            product.Id,
            product.Name,
            product.Brand,
            product.Unit,
            product.PackageContent,
            PriceNormalizer.BaseOf(product.Unit)?.Unit,
            Summarize(points),
            points);
    }

    public async Task<ProductPriceComparisonResponse> GetComparisonAsync(
        int productId, PriceHistoryQuery query, CancellationToken cancellationToken)
    {
        var product = await FindProductAsync(productId, cancellationToken);
        var points = await LoadPointsAsync(productId, new PriceHistoryQuery { From = query.From, To = query.To }, cancellationToken);

        var stores = points
            .GroupBy(p => new { p.StoreId, p.StoreName })
            .Select(g =>
            {
                var last = g.OrderBy(p => p.Date).ThenBy(p => p.ObservationId).Last();
                return new
                {
                    g.Key.StoreId,
                    g.Key.StoreName,
                    Count = g.Count(),
                    Last = new PriceReference(last.EffectiveUnitPrice, last.Date, last.StoreName),
                    Lowest = g.Min(p => p.EffectiveUnitPrice),
                    Average = Average(g.Select(p => p.EffectiveUnitPrice)),
                    last.NormalizedPrice
                };
            })
            .OrderBy(s => s.Last.Price)
            .ThenBy(s => s.StoreName)
            .ToList();

        var cheapest = stores.Count > 0 ? stores[0].Last.Price : 0m;
        var comparisons = stores
            .Select(s => new StorePriceComparison(
                s.StoreId, s.StoreName, s.Count, s.Last, s.Lowest, s.Average, s.NormalizedPrice,
                Percent(cheapest, s.Last.Price) ?? 0m))
            .ToList();

        var normalizedUnit = PriceNormalizer.BaseOf(product.Unit)?.Unit;
        var alternatives = product.CategoryId is { } categoryId && normalizedUnit is { } unit
            ? await LoadAlternativesAsync(product.Id, categoryId, unit, query, cancellationToken)
            : [];

        return new ProductPriceComparisonResponse(product.Id, product.Name, normalizedUnit, comparisons, alternatives);
    }

    public async Task<IReadOnlyList<UnassignedItemGroup>> ListUnassignedAsync(CancellationToken cancellationToken)
    {
        var items = await db.ReceiptItems.AsNoTracking()
            .Where(i => i.ProductId == null && i.Receipt.Status == ReceiptStatus.Confirmed)
            .Select(i => new { i.OriginalDescription, i.Total, i.Receipt.IssuedAt, StoreName = i.Receipt.Store.TradeName })
            .ToListAsync(cancellationToken);

        var groups = items
            .GroupBy(i => TextNormalizer.NormalizeDescription(i.OriginalDescription))
            .Select(g => new
            {
                Key = g.Key,
                Latest = g.OrderByDescending(i => i.IssuedAt).First(),
                Count = g.Count(),
                Total = g.Sum(i => i.Total),
                Stores = g.Select(i => i.StoreName).Distinct().OrderBy(s => s).Take(3).ToList()
            })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Latest.IssuedAt)
            .Take(UnassignedLimit)
            .ToList();

        var keys = groups.Select(g => g.Key).ToList();
        var suggestions = (await db.ProductAliases.AsNoTracking()
                .Where(a => keys.Contains(a.NormalizedDescription) && a.Confirmed && a.Product.Status == ProductStatus.Active)
                .Select(a => new { a.NormalizedDescription, a.ProductId, a.Product.Name })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.NormalizedDescription)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        return groups
            .Select(g =>
            {
                suggestions.TryGetValue(g.Key, out var suggestion);
                return new UnassignedItemGroup(
                    g.Key, g.Latest.OriginalDescription, g.Count, g.Latest.IssuedAt, g.Total, g.Stores,
                    suggestion?.ProductId, suggestion?.Name);
            })
            .ToList();
    }

    public async Task<AssignItemsResponse> AssignAsync(AssignItemsRequest request, CancellationToken cancellationToken)
    {
        var productId = request.ProductId!.Value;
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
                      ?? throw new BusinessRuleException(
                          "Produto inexistente.",
                          new Dictionary<string, string[]> { ["productId"] = ["O produto informado não existe."] });

        var normalized = TextNormalizer.NormalizeDescription(request.Description);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var candidates = await db.ReceiptItems
            .Include(i => i.Receipt)
            .Include(i => i.PriceObservation)
            .Where(i => i.ProductId == null && i.Receipt.Status == ReceiptStatus.Confirmed)
            .ToListAsync(cancellationToken);
        var items = candidates.Where(i => TextNormalizer.NormalizeDescription(i.OriginalDescription) == normalized).ToList();
        if (items.Count == 0)
        {
            throw new NotFoundException("Nenhum item sem produto com essa descrição.");
        }

        foreach (var item in items)
        {
            item.ProductId = productId;
            item.Receipt.UpdatedAt = now;
        }

        // A associação feita pelo usuário vira descrição alternativa confirmada (RN06).
        var alias = await db.ProductAliases
            .SingleOrDefaultAsync(a => a.ProductId == productId && a.NormalizedDescription == normalized, cancellationToken);
        if (alias is null)
        {
            db.ProductAliases.Add(new ProductAlias
            {
                ProductId = productId,
                Description = items.OrderByDescending(i => i.Receipt.IssuedAt).First().OriginalDescription,
                NormalizedDescription = normalized,
                Confirmed = true,
                Source = AliasSource.Receipt,
                CreatedAt = now
            });
        }
        else
        {
            alias.Confirmed = true;
        }

        await priceRecorder.SyncItemsAsync(items, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AssignItemsResponse(items.Count, product.Id, product.Name);
    }

    private async Task<Product> FindProductAsync(int productId, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
        ?? throw new NotFoundException($"Produto {productId} não encontrado.");

    private async Task<List<PricePoint>> LoadPointsAsync(int productId, PriceHistoryQuery query, CancellationToken cancellationToken)
    {
        if (query is { From: { } from, To: { } to } && from > to)
        {
            throw new BusinessRuleException(
                "Período inválido.",
                new Dictionary<string, string[]> { ["to"] = ["A data final deve ser igual ou posterior à inicial."] });
        }

        var observations = db.PriceObservations.AsNoTracking().Where(o => o.ProductId == productId);
        if (query.From is { } start)
        {
            var startDate = start.ToDateTime(TimeOnly.MinValue);
            observations = observations.Where(o => o.ObservedAt >= startDate);
        }

        if (query.To is { } end)
        {
            var endExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            observations = observations.Where(o => o.ObservedAt < endExclusive);
        }

        if (query.StoreId is { } storeId)
        {
            observations = observations.Where(o => o.StoreId == storeId);
        }

        var rows = await observations
            .OrderBy(o => o.ObservedAt)
            .ThenBy(o => o.Id)
            .Select(o => new
            {
                o.Id,
                o.ReceiptItem.ReceiptId,
                o.ObservedAt,
                o.StoreId,
                StoreName = o.Store.TradeName,
                o.Quantity,
                o.ReceiptItem.Unit,
                o.UnitPrice,
                o.TotalPrice,
                o.NormalizedPrice,
                o.NormalizedUnit
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new PricePoint(
                r.Id, r.ReceiptId, r.ObservedAt, r.StoreId, r.StoreName, r.Quantity, r.Unit, r.UnitPrice, r.TotalPrice,
                PriceNormalizer.EffectiveUnitPrice(r.TotalPrice, r.Quantity), r.NormalizedPrice, r.NormalizedUnit))
            .ToList();
    }

    private async Task<List<AlternativeProductPrice>> LoadAlternativesAsync(
        int productId, int categoryId, NormalizedUnit unit, PriceHistoryQuery query, CancellationToken cancellationToken)
    {
        var observations = db.PriceObservations.AsNoTracking()
            .Where(o => o.ProductId != productId
                        && o.Product.CategoryId == categoryId
                        && o.Product.Status == ProductStatus.Active
                        && o.NormalizedUnit == unit
                        && o.NormalizedPrice != null);
        if (query.From is { } start)
        {
            var startDate = start.ToDateTime(TimeOnly.MinValue);
            observations = observations.Where(o => o.ObservedAt >= startDate);
        }

        if (query.To is { } end)
        {
            var endExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            observations = observations.Where(o => o.ObservedAt < endExclusive);
        }

        var rows = await observations
            .Select(o => new
            {
                o.Id,
                o.ProductId,
                o.Product.Name,
                o.Product.Brand,
                o.NormalizedPrice,
                o.ObservedAt,
                StoreName = o.Store.TradeName
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ProductId)
            .Select(g => g.OrderBy(r => r.ObservedAt).ThenBy(r => r.Id).Last())
            .Select(r => new AlternativeProductPrice(r.ProductId, r.Name, r.Brand, r.NormalizedPrice!.Value, unit, r.ObservedAt, r.StoreName))
            .OrderBy(a => a.LastNormalized)
            .Take(AlternativesLimit)
            .ToList();
    }

    private static PriceSummary Summarize(IReadOnlyList<PricePoint> points)
    {
        if (points.Count == 0)
        {
            return new PriceSummary(0, null, null, null, null, null, null, null, null, null);
        }

        var first = points[0];
        var last = points[^1];
        var lowest = points.OrderBy(p => p.EffectiveUnitPrice).ThenByDescending(p => p.Date).First();
        var highest = points.OrderByDescending(p => p.EffectiveUnitPrice).ThenByDescending(p => p.Date).First();

        // Indicadores normalizados só quando todas as compras do período têm a mesma unidade-base.
        var normalizedUnits = points.Select(p => p.NormalizedUnit).Distinct().ToList();
        var normalized = normalizedUnits is [{ } unit] && points.All(p => p.NormalizedPrice is not null)
            ? points.Select(p => p.NormalizedPrice!.Value).ToList()
            : null;

        return new PriceSummary(
            points.Count,
            Reference(last),
            Reference(lowest),
            Reference(highest),
            Average(points.Select(p => p.EffectiveUnitPrice)),
            points.Count > 1 ? Percent(first.EffectiveUnitPrice, last.EffectiveUnitPrice) : null,
            normalized is null ? null : normalizedUnits[0],
            normalized is null ? null : last.NormalizedPrice,
            normalized?.Min(),
            normalized is null ? null : Average(normalized));
    }

    private static PriceReference Reference(PricePoint point) => new(point.EffectiveUnitPrice, point.Date, point.StoreName);

    private static decimal Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return DecimalPrecision.Round(list.Sum() / list.Count, DecimalPrecision.UnitPrice);
    }

    /// <summary>Variação percentual de <paramref name="from"/> para <paramref name="to"/>, com 2 casas.</summary>
    private static decimal? Percent(decimal from, decimal to) =>
        from == 0 ? null : DecimalPrecision.Round((to - from) / from * 100m, 2);
}
