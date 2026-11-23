using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Api.Services.Receipts;

/// <summary>
/// Mantém as observações de preço de uma nota coerentes com seus itens (RN03, RN04, RN05):
/// cada item de nota confirmada e associado a um produto tem exatamente uma observação;
/// itens sem produto, ou de notas não confirmadas, não têm observação. Atua somente sobre
/// a nota informada — observações de outras notas nunca são alteradas.
/// </summary>
/// <remarks>
/// O preço normalizado (por kg, litro ou unidade) será calculado na Fase 5; até lá permanece nulo.
/// </remarks>
public sealed class PriceObservationRecorder(AppDbContext db)
{
    /// <summary>Requer a nota com <c>Items</c> e <c>Items.PriceObservation</c> carregados e rastreados.</summary>
    public void Sync(Receipt receipt, DateTime nowUtc)
    {
        foreach (var item in receipt.Items)
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
            }
            else if (item.PriceObservation is { } existing)
            {
                db.PriceObservations.Remove(existing);
                item.PriceObservation = null;
            }
        }
    }
}
