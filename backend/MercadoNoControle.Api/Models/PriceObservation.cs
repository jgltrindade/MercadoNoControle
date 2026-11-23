namespace MercadoNoControle.Api.Models;

/// <summary>
/// Observação histórica de preço gerada a partir de um item de compra confirmada
/// e associado a um produto (especificação: HistoricoPreco; RN03).
/// </summary>
public class PriceObservation
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Item de origem. Cada item gera no máximo uma observação.</summary>
    public int ReceiptItemId { get; set; }

    public ReceiptItem ReceiptItem { get; set; } = null!;

    public int StoreId { get; set; }

    public Store Store { get; set; } = null!;

    /// <summary>Data da compra (emissão da nota).</summary>
    public DateTime ObservedAt { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }

    /// <summary>Preço por kg, litro ou unidade, quando a embalagem permite (RN08).</summary>
    public decimal? NormalizedPrice { get; set; }

    public NormalizedUnit? NormalizedUnit { get; set; }

    public DateTime CreatedAt { get; set; }
}
