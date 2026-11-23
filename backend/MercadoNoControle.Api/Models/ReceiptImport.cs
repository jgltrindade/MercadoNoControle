namespace MercadoNoControle.Api.Models;

/// <summary>
/// Importação de nota por imagem (especificação: ProcessamentoIA). Possui
/// identificador e estado próprios e não exige uma nota fiscal definitiva:
/// <see cref="ReceiptId"/> só é preenchido após a confirmação do usuário.
/// </summary>
public class ReceiptImport
{
    public Guid Id { get; set; }

    /// <summary>Nota criada a partir desta importação, após confirmação.</summary>
    public int? ReceiptId { get; set; }

    public Receipt? Receipt { get; set; }

    public ReceiptImportStatus Status { get; set; } = ReceiptImportStatus.Pending;

    /// <summary>Caminho relativo da imagem original dentro do diretório de uploads.</summary>
    public required string ImagePath { get; set; }

    public string? OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long FileSize { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    /// <summary>Resultado estruturado retornado pelo provedor, como JSON.</summary>
    public string? ResultJson { get; set; }

    public string? Error { get; set; }

    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Data da última tentativa de processamento.</summary>
    public DateTime? ProcessedAt { get; set; }
}
