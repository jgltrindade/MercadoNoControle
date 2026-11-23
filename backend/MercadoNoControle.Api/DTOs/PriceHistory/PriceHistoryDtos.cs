using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Api.DTOs.PriceHistory;

public sealed class PriceHistoryQuery
{
    /// <summary>Data inicial (inclusive) da compra.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Data final (inclusive) da compra.</summary>
    public DateOnly? To { get; set; }

    public int? StoreId { get; set; }
}

/// <summary>Uma compra do produto (observação de preço).</summary>
public sealed record PricePoint(
    int ObservationId,
    int ReceiptId,
    DateTime Date,
    int StoreId,
    string StoreName,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal TotalPrice,
    /// <summary>Total pago ÷ quantidade (considera descontos).</summary>
    decimal EffectiveUnitPrice,
    decimal? NormalizedPrice,
    NormalizedUnit? NormalizedUnit);

/// <summary>Referência a uma compra usada em um indicador (menor/maior/último preço).</summary>
public sealed record PriceReference(decimal Price, DateTime Date, string StoreName);

/// <summary>Indicadores calculados em decimal no backend (nunca com AVG do SQLite).</summary>
public sealed record PriceSummary(
    int Count,
    PriceReference? Last,
    PriceReference? Lowest,
    PriceReference? Highest,
    decimal? Average,
    /// <summary>Variação entre a primeira e a última compra do período, em %.</summary>
    decimal? VariationPercent,
    NormalizedUnit? NormalizedUnit,
    decimal? LastNormalized,
    decimal? LowestNormalized,
    decimal? AverageNormalized);

public sealed record ProductPriceHistoryResponse(
    int ProductId,
    string ProductName,
    string? Brand,
    MeasurementUnit Unit,
    decimal? PackageContent,
    /// <summary>Unidade-base do preço normalizado do produto, quando aplicável.</summary>
    NormalizedUnit? NormalizedUnit,
    PriceSummary Summary,
    IReadOnlyList<PricePoint> Points);

public sealed record StorePriceComparison(
    int StoreId,
    string StoreName,
    int Count,
    PriceReference Last,
    decimal Lowest,
    decimal Average,
    decimal? LastNormalized,
    /// <summary>Diferença do último preço desta loja em relação ao menor último preço entre as lojas, em %.</summary>
    decimal DifferenceToCheapestPercent);

/// <summary>Produto da mesma categoria com preço normalizado na mesma unidade (comparável, não equivalente).</summary>
public sealed record AlternativeProductPrice(
    int ProductId,
    string Name,
    string? Brand,
    decimal LastNormalized,
    NormalizedUnit NormalizedUnit,
    DateTime Date,
    string StoreName);

public sealed record ProductPriceComparisonResponse(
    int ProductId,
    string ProductName,
    NormalizedUnit? NormalizedUnit,
    IReadOnlyList<StorePriceComparison> Stores,
    IReadOnlyList<AlternativeProductPrice> Alternatives);

/// <summary>Itens de compras confirmadas ainda sem produto, agrupados pela descrição normalizada.</summary>
public sealed record UnassignedItemGroup(
    string NormalizedDescription,
    string Description,
    int Occurrences,
    DateTime LastSeenAt,
    decimal TotalSpent,
    IReadOnlyList<string> Stores,
    int? SuggestedProductId,
    string? SuggestedProductName);

public sealed class AssignItemsRequest
{
    [Required(ErrorMessage = "Informe a descrição.")]
    [MaxLength(250)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o produto.")]
    public int? ProductId { get; set; }
}

public sealed record AssignItemsResponse(int UpdatedItems, int ProductId, string ProductName);
