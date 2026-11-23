namespace MercadoNoControle.Api.Models;

/// <summary>Produto padronizado do catálogo (especificação: Produto).</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>Nome padronizado (ex.: "Leite Integral 1L").</summary>
    public required string Name { get; set; }

    public string? Brand { get; set; }

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    public string? Barcode { get; set; }

    /// <summary>Unidade de medida do conteúdo da embalagem.</summary>
    public MeasurementUnit Unit { get; set; } = MeasurementUnit.UN;

    /// <summary>Conteúdo da embalagem na <see cref="Unit"/> (ex.: 1 para 1 L, 500 para 500 g).</summary>
    public decimal? PackageContent { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Active;

    public DateTime CreatedAt { get; set; }

    public ICollection<ProductAlias> Aliases { get; set; } = [];

    public ICollection<ReceiptItem> ReceiptItems { get; set; } = [];

    public ICollection<PriceObservation> PriceObservations { get; set; } = [];
}
