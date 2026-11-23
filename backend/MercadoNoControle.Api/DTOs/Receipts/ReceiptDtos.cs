using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Validators;

namespace MercadoNoControle.Api.DTOs.Receipts;

/// <summary>Item da nota como impresso. O total do item já é líquido do desconto do item.</summary>
public sealed class ReceiptItemRequest
{
    /// <summary>Id de um item existente (edição). Nulo para itens novos.</summary>
    public int? Id { get; set; }

    [Required(ErrorMessage = "Informe a descrição.")]
    [MaxLength(250)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Produto associado; nulo mantém o item sem identificação (RN06).</summary>
    public int? ProductId { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade deve ser maior que zero.")]
    [DecimalPlaces(DecimalPrecision.Quantity)]
    public decimal Quantity { get; set; }

    [MaxLength(10)]
    public string? Unit { get; set; }

    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Preço unitário inválido.")]
    [DecimalPlaces(DecimalPrecision.UnitPrice)]
    public decimal UnitPrice { get; set; }

    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Desconto inválido.")]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal Discount { get; set; }

    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Total do item inválido.")]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal Total { get; set; }
}

/// <summary>Dados gerais da nota como impressos, mais os itens.</summary>
public sealed class ReceiptRequest
{
    [Required(ErrorMessage = "Informe o supermercado.")]
    public int? StoreId { get; set; }

    [MaxLength(20)]
    public string? Number { get; set; }

    [MaxLength(10)]
    public string? Series { get; set; }

    [AccessKey]
    [MaxLength(60)]
    public string? AccessKey { get; set; }

    /// <summary>Data e hora de emissão no horário local da nota (sem fuso).</summary>
    [Required(ErrorMessage = "Informe a data de emissão.")]
    public DateTime? IssuedAt { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal ProductsTotal { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal DiscountTotal { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal AdditionalCharges { get; set; }

    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPlaces(DecimalPrecision.Money)]
    public decimal Total { get; set; }

    [MaxLength(60)]
    public string? PaymentMethod { get; set; }

    [MaxLength(500, ErrorMessage = "A nota pode ter no máximo 500 itens.")]
    public List<ReceiptItemRequest> Items { get; set; } = [];
}

public sealed class ConfirmReceiptRequest
{
    /// <summary>Confirma mesmo com alertas (divergências de totais ou possível duplicidade).</summary>
    public bool AcknowledgeWarnings { get; set; }
}

public sealed class ReceiptQuery : PageQuery
{
    public int? StoreId { get; set; }

    public ReceiptStatus? Status { get; set; }

    /// <summary>Data inicial (inclusive) da emissão.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Data final (inclusive) da emissão.</summary>
    public DateOnly? To { get; set; }
}

public sealed record ReceiptListItem(
    int Id,
    int StoreId,
    string StoreName,
    string? Number,
    string? Series,
    DateTime IssuedAt,
    decimal Total,
    int ItemCount,
    int UnidentifiedItemCount,
    ReceiptStatus Status,
    bool HasImage,
    DateTime CreatedAt);

public sealed record ReceiptItemResponse(
    int Id,
    int LineNumber,
    string Description,
    int? ProductId,
    string? ProductName,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal Discount,
    decimal Total);

/// <summary>Alerta de consistência (RN10) ou de identificação; não impede salvar.</summary>
public sealed record ReceiptWarning(string Code, string Message, int? LineNumber = null);

/// <summary>Nota confirmada que pode ser a mesma compra (RN02).</summary>
public sealed record DuplicateCandidate(
    int Id,
    string StoreName,
    string? Number,
    string? Series,
    DateTime IssuedAt,
    decimal Total,
    string Reason);

public sealed record ReceiptResponse(
    int Id,
    int StoreId,
    string StoreName,
    string? StoreCnpj,
    string? Number,
    string? Series,
    string? AccessKey,
    DateTime IssuedAt,
    decimal ProductsTotal,
    decimal DiscountTotal,
    decimal AdditionalCharges,
    decimal Total,
    string? PaymentMethod,
    ReceiptStatus Status,
    bool HasImage,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<ReceiptItemResponse> Items,
    IReadOnlyList<ReceiptWarning> Warnings,
    IReadOnlyList<DuplicateCandidate> PossibleDuplicates);
