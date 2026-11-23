namespace MercadoNoControle.Api.DTOs.Settings;

/// <summary>Leitura automática de notas: provedor e modelo configurados (a chave nunca é exposta).</summary>
public sealed record ReaderSettingsResponse(
    bool Available,
    string Provider,
    string? Model,
    string? Effort,
    int TimeoutSeconds,
    string? Message);

public sealed record DataCountsResponse(
    int Stores,
    int Categories,
    int Products,
    int ConfirmedReceipts,
    int DraftReceipts,
    int PriceObservations,
    int OpenImports);

/// <summary>Informações gerais da instalação (Configurações → Geral).</summary>
public sealed record SystemInfoResponse(
    string Version,
    string Environment,
    bool AuthenticationEnabled,
    string DatabaseFile,
    long DatabaseSizeBytes,
    string UploadsDirectory,
    DataCountsResponse Counts,
    ReaderSettingsResponse Reader);

public sealed record ImageFileResponse(string Path, long SizeBytes, DateTime ModifiedAt);

/// <summary>
/// Imagens no diretório de uploads. Órfãs: não usadas por nenhuma nota nem por leitura em andamento
/// (ex.: imagem removida/substituída em uma nota ou nota excluída).
/// </summary>
public sealed record ImagesSummaryResponse(
    int TotalFiles,
    long TotalBytes,
    int ReferencedFiles,
    int MissingFiles,
    long OrphanBytes,
    IReadOnlyList<ImageFileResponse> Orphans);

public sealed record DeleteOrphansResponse(int DeletedFiles, long FreedBytes);
