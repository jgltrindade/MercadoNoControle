namespace MercadoNoControle.Api.Models;

/// <summary>Item de uma nota fiscal (especificação: ItemNotaFiscal).</summary>
public class ReceiptItem
{
    public int Id { get; set; }

    public int ReceiptId { get; set; }

    public Receipt Receipt { get; set; } = null!;

    /// <summary>Produto associado; nulo enquanto o item não for identificado (RN06).</summary>
    public int? ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Ordem do item na nota.</summary>
    public int LineNumber { get; set; }

    /// <summary>Descrição original da nota, sempre preservada.</summary>
    public required string OriginalDescription { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Unidade como impressa na nota (ex.: "UN", "KG").</summary>
    public string? Unit { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public PriceObservation? PriceObservation { get; set; }
}
