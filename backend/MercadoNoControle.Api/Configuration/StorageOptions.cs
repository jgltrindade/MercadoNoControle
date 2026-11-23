namespace MercadoNoControle.Api.Configuration;

/// <summary>
/// Locais de armazenamento do banco SQLite e das imagens. Caminhos relativos são
/// resolvidos a partir do diretório do projeto/executável (ContentRoot).
/// Podem ser sobrescritos por variáveis de ambiente: <c>Storage__DatabasePath</c>, <c>Storage__UploadsPath</c>.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    public string DatabasePath { get; set; } = "../../data/mercado-no-controle.db";

    public string UploadsPath { get; set; } = "../../uploads";
}

/// <summary>Caminhos absolutos resolvidos na inicialização.</summary>
public sealed record StoragePaths(string DatabaseFile, string UploadsDirectory)
{
    public static StoragePaths Resolve(StorageOptions options, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            throw new InvalidOperationException("Storage:DatabasePath não configurado.");
        }

        if (string.IsNullOrWhiteSpace(options.UploadsPath))
        {
            throw new InvalidOperationException("Storage:UploadsPath não configurado.");
        }

        return new StoragePaths(
            Path.GetFullPath(options.DatabasePath, contentRootPath),
            Path.GetFullPath(options.UploadsPath, contentRootPath));
    }

    public string ConnectionString => $"Data Source={DatabaseFile}";

    /// <summary>Garante a existência dos diretórios do banco e das imagens.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        Directory.CreateDirectory(UploadsDirectory);
    }
}
