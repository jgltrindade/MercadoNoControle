using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Api.DTOs.Imports;

/// <summary>Disponibilidade da leitura automática (sem expor configuração sensível).</summary>
public sealed record ReaderStatusResponse(bool Available, string Provider, string? Model, string? Message);

/// <summary>Item sugerido para conferência, a partir do que a IA leu.</summary>
public sealed record DraftItem(
    int LineNumber,
    string Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? Discount,
    decimal? Total,
    int? ProductId,
    string? ProductName,
    int MatchCount);

/// <summary>
/// Dados lidos pela IA, já validados e associados ao cadastro quando possível
/// (supermercado pelo CNPJ, produtos pelas descrições alternativas confirmadas).
/// Nada é gravado até a confirmação do usuário.
/// </summary>
public sealed record ReceiptDraft(
    int? StoreId,
    string? StoreName,
    string? ExtractedStoreName,
    string? ExtractedCnpj,
    string? Number,
    string? Series,
    string? AccessKey,
    DateTime? IssuedAt,
    decimal? ProductsTotal,
    decimal? DiscountTotal,
    decimal? AdditionalCharges,
    decimal? Total,
    string? PaymentMethod,
    IReadOnlyList<DraftItem> Items,
    IReadOnlyList<ReceiptWarning> Warnings);

public sealed record ReceiptImportResponse(
    Guid Id,
    ReceiptImportStatus Status,
    string? OriginalFileName,
    long FileSize,
    string? Provider,
    string? Model,
    string? Error,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime? ProcessedAt,
    int? ReceiptId,
    ReceiptDraft? Draft);

/// <summary>Dados conferidos pelo usuário para registrar a compra a partir da importação.</summary>
public sealed class ConfirmImportRequest : ReceiptRequest
{
    /// <summary>Confirma mesmo com alertas (divergências de totais ou possível duplicidade).</summary>
    public bool AcknowledgeWarnings { get; set; }
}
