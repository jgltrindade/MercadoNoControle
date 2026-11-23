using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.Validators;

namespace MercadoNoControle.Api.DTOs.Stores;

/// <summary>Supermercado com indicadores calculados somente sobre compras confirmadas (RN09).</summary>
public sealed record StoreResponse(
    int Id,
    string TradeName,
    string? LegalName,
    string? Cnpj,
    string? Address,
    string? City,
    string? State,
    string? Notes,
    DateTime CreatedAt,
    int ReceiptCount,
    decimal TotalSpent,
    DateTime? LastPurchaseAt);

public sealed class StoreRequest
{
    [Required(ErrorMessage = "Informe o nome fantasia.")]
    [MaxLength(150)]
    public string TradeName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? LegalName { get; set; }

    /// <summary>Com ou sem máscara; numérico ou alfanumérico.</summary>
    [Cnpj]
    [MaxLength(18)]
    public string? Cnpj { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [BrazilianState]
    public string? State { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public sealed class StoreQuery : PageQuery
{
    [BrazilianState]
    public string? State { get; set; }
}
