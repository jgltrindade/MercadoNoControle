using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Validators;

namespace MercadoNoControle.Api.DTOs.Products;

public sealed record ProductListItem(
    int Id,
    string Name,
    string? Brand,
    int? CategoryId,
    string? CategoryName,
    string? Barcode,
    MeasurementUnit Unit,
    decimal? PackageContent,
    ProductStatus Status,
    int AliasCount,
    DateTime CreatedAt);

public sealed record ProductAliasResponse(
    int Id,
    string Description,
    bool Confirmed,
    AliasSource Source,
    DateTime CreatedAt);

public sealed record ProductResponse(
    int Id,
    string Name,
    string? Brand,
    int? CategoryId,
    string? CategoryName,
    string? Barcode,
    MeasurementUnit Unit,
    decimal? PackageContent,
    ProductStatus Status,
    DateTime CreatedAt,
    IReadOnlyList<ProductAliasResponse> Aliases);

public sealed class ProductRequest
{
    [Required(ErrorMessage = "Informe o nome padronizado.")]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Brand { get; set; }

    public int? CategoryId { get; set; }

    [Gtin]
    public string? Barcode { get; set; }

    [Required(ErrorMessage = "Informe a unidade de medida.")]
    public MeasurementUnit? Unit { get; set; }

    /// <summary>Conteúdo da embalagem na unidade informada (ex.: 1 para 1 L; 500 para 500 g).</summary>
    [Range(typeof(decimal), "0.0001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O conteúdo deve estar entre 0,0001 e 1.000.000.")]
    [DecimalPlaces(DecimalPrecision.PackageContent)]
    public decimal? PackageContent { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Active;
}

public sealed class ProductAliasRequest
{
    [Required(ErrorMessage = "Informe a descrição.")]
    [MaxLength(250)]
    public string Description { get; set; } = string.Empty;
}

public sealed class ProductQuery : PageQuery
{
    public int? CategoryId { get; set; }

    public ProductStatus? Status { get; set; }
}
