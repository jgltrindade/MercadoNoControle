namespace MercadoNoControle.Api.Services.Ai;

/// <summary>
/// Leitura de notas fiscais por um provedor de IA multimodal (especificação 5.4).
/// A implementação concreta é escolhida por configuração; as regras de negócio dependem
/// somente desta interface.
/// </summary>
public interface IReceiptReader
{
    /// <summary>Nome do provedor (registrado em cada importação).</summary>
    string ProviderName { get; }

    /// <summary>Modelo utilizado, quando houver.</summary>
    string? ModelName { get; }

    /// <summary>Indica se a leitura automática está disponível (ex.: chave configurada).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Extrai os dados da imagem. Falhas conhecidas (provedor indisponível, tempo esgotado,
    /// resposta inválida etc.) são lançadas como <see cref="ReceiptReaderException"/>.
    /// </summary>
    Task<ReceiptExtractionResult> ExtractAsync(Stream image, CancellationToken cancellationToken);
}

/// <summary>Resultado da leitura: dados estruturados e o JSON original devolvido pelo provedor.</summary>
public sealed record ReceiptExtractionResult(ReceiptExtraction Extraction, string RawJson, string Provider, string? Model);

/// <summary>
/// Contrato de retorno da IA (especificação 5.3). Campos não identificados são nulos;
/// os valores são transcritos como impressos, sem cálculos.
/// </summary>
public sealed record ReceiptExtraction
{
    /// <summary>Falso quando a imagem não é uma nota fiscal ou está ilegível.</summary>
    public bool IsReceipt { get; init; }

    public string? UnreadableReason { get; init; }

    public ExtractedStore? Store { get; init; }

    public ExtractedReceiptData? Receipt { get; init; }

    public IReadOnlyList<ExtractedItem> Items { get; init; } = [];
}

public sealed record ExtractedStore(string? Name, string? Cnpj);

public sealed record ExtractedReceiptData(
    string? Number,
    string? Series,
    string? AccessKey,
    string? IssueDate,
    decimal? TotalProducts,
    decimal? Discount,
    decimal? AdditionalCharges,
    decimal? Total,
    string? PaymentMethod);

/// <summary>Item como impresso. <see cref="TotalPrice"/> é o valor da linha, antes do desconto do item.</summary>
public sealed record ExtractedItem(
    string? Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? Discount,
    decimal? TotalPrice);

public enum ReceiptReaderErrorKind
{
    /// <summary>Leitura automática não configurada (sem chave) ou credencial recusada.</summary>
    NotConfigured,

    /// <summary>Imagem em formato não suportado ou grande demais para o provedor.</summary>
    InvalidImage,

    /// <summary>Provedor indisponível, limite de uso atingido ou falha de rede.</summary>
    Unavailable,

    /// <summary>Tempo de leitura esgotado.</summary>
    Timeout,

    /// <summary>Resposta vazia, incompleta ou fora do formato esperado.</summary>
    InvalidResponse,

    /// <summary>O provedor recusou a solicitação.</summary>
    Refused
}

public sealed class ReceiptReaderException(ReceiptReaderErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ReceiptReaderErrorKind Kind { get; } = kind;
}
