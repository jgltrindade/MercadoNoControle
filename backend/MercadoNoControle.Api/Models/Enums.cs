namespace MercadoNoControle.Api.Models;

/// <summary>Unidades de medida previstas para produtos (especificação 4.4).</summary>
public enum MeasurementUnit
{
    UN,
    KG,
    G,
    L,
    ML,
    CX,
    PCT,
    DZ
}

/// <summary>Situação do cadastro de um produto.</summary>
public enum ProductStatus
{
    Active,
    Inactive
}

/// <summary>Origem de uma descrição alternativa de produto.</summary>
public enum AliasSource
{
    Manual,
    Receipt,
    AiSuggestion
}

/// <summary>
/// Situação da nota fiscal. Somente notas <see cref="Confirmed"/> participam
/// de relatórios, dashboard e histórico de preços (RN01, RN09).
/// </summary>
public enum ReceiptStatus
{
    Draft,
    Confirmed
}

/// <summary>Unidade-base usada para o preço normalizado (RN08).</summary>
public enum NormalizedUnit
{
    Kilogram,
    Liter,
    Unit
}

/// <summary>
/// Estado de uma importação de nota por imagem. A importação possui identidade
/// própria e existe antes de qualquer nota fiscal definitiva.
/// </summary>
public enum ReceiptImportStatus
{
    /// <summary>Imagem recebida e armazenada, aguardando leitura.</summary>
    Pending,

    /// <summary>Leitura pelo provedor de IA em andamento.</summary>
    Processing,

    /// <summary>Dados extraídos, aguardando conferência do usuário.</summary>
    AwaitingReview,

    /// <summary>Leitura falhou; pode ser reprocessada ou cadastrada manualmente.</summary>
    Failed,

    /// <summary>Usuário confirmou e a nota fiscal definitiva foi criada.</summary>
    Confirmed,

    /// <summary>Usuário descartou a importação.</summary>
    Discarded
}
