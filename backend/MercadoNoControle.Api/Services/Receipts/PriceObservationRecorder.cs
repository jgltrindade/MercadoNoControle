using System.Linq.Expressions;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.PriceHistory;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Receipts;

/// <summary>
/// Mantém as observações de preço coerentes com os itens (RN03, RN04, RN05): cada item de nota
/// confirmada e associado a um produto tem exatamente uma observação, com o preço normalizado
/// quando possível (RN08); itens sem produto, ou de notas não confirmadas, não têm observação.
/// Atua somente sobre os itens informados — observações de outras notas nunca são alteradas.
/// </summary>
public sealed class PriceObservationRecorder(AppDbContext db)
{
    /// <summary>Requer a nota com <c>Items</c> e <c>Items.PriceObservation</c> carregados e rastreados.</summary>
    public async Task SyncAsync(Receipt receipt, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync(receipt.Items, cancellationToken);
        foreach (var item in receipt.Items)
        {
            SyncItem(item, receipt, products, nowUtc);
        }
    }

    /// <summary>Requer cada item com <c>Receipt</c> e <c>PriceObservation</c> carregados e rastreados.</summary>
    public async Task SyncItemsAsync(IReadOnlyCollection<ReceiptItem> items, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync(items, cancellationToken);
        foreach (var item in items)
        {
            SyncItem(item, item.Receipt, products, nowUtc);
        }
    }

    /// <summary>
    /// Recalcula o preço normalizado das observações (todas, ou as do filtro) a partir do cadastro
    /// atual do produto e grava. Usado após alterar unidade/conteúdo do produto e na inicialização.
    /// Retorna quantas observações mudaram.
    /// </summary>
    public async Task<int> RecalculateAsync(
        Expression<Func<PriceObservation, bool>>? filter, CancellationToken cancellationToken)
    {
        var query = db.PriceObservations.Include(o => o.ReceiptItem).Include(o => o.Product).AsQueryable();
        if (filter is not null)
        {
            query = query.Where(filter);
        }

        var changed = 0;
        foreach (var observation in await query.ToListAsync(cancellationToken))
        {
            var normalized = PriceNormalizer.Normalize(
                observation.Product.Unit,
                observation.Product.PackageContent,
                observation.ReceiptItem.Unit,
                observation.ReceiptItem.Quantity,
                observation.ReceiptItem.Total);

            if (observation.NormalizedPrice != normalized?.Price || observation.NormalizedUnit != normalized?.Unit)
            {
                observation.NormalizedPrice = normalized?.Price;
                observation.NormalizedUnit = normalized?.Unit;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private void SyncItem(ReceiptItem item, Receipt receipt, IReadOnlyDictionary<int, ProductUnit> products, DateTime nowUtc)
    {
        if (receipt.Status == ReceiptStatus.Confirmed && item.ProductId is { } productId)
        {
            var observation = item.PriceObservation;
            if (observation is null)
            {
                observation = new PriceObservation { CreatedAt = nowUtc, ReceiptItem = item };
                item.PriceObservation = observation;
                db.PriceObservations.Add(observation);
            }

            observation.ProductId = productId;
            observation.StoreId = receipt.StoreId;
            observation.ObservedAt = receipt.IssuedAt;
            observation.Quantity = item.Quantity;
            observation.UnitPrice = item.UnitPrice;
            observation.TotalPrice = item.Total;

            var normalized = products.TryGetValue(productId, out var product)
                ? PriceNormalizer.Normalize(product.Unit, product.PackageContent, item.Unit, item.Quantity, item.Total)
                : null;
            observation.NormalizedPrice = normalized?.Price;
            observation.NormalizedUnit = normalized?.Unit;
        }
        else if (item.PriceObservation is { } existing)
        {
            db.PriceObservations.Remove(existing);
            item.PriceObservation = null;
        }
    }

    private async Task<Dictionary<int, ProductUnit>> LoadProductsAsync(IEnumerable<ReceiptItem> items, CancellationToken cancellationToken)
    {
        var ids = items.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new ProductUnit(p.Unit, p.PackageContent), cancellationToken);
    }

    private sealed record ProductUnit(MeasurementUnit Unit, decimal? PackageContent);
}
