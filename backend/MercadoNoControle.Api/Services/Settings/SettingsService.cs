using System.Reflection;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Settings;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Api.Services.Settings;

public interface ISettingsService
{
    Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken);

    Task<ImagesSummaryResponse> GetImagesAsync(CancellationToken cancellationToken);

    Task<DeleteOrphansResponse> DeleteOrphanImagesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Configurações (especificação 4.8): informações da instalação, provedor de IA e gerenciamento
/// das imagens armazenadas.
/// </summary>
public sealed class SettingsService(
    AppDbContext db,
    StoragePaths paths,
    IReceiptReader reader,
    IOptions<ReceiptReaderOptions> readerOptions,
    IOptions<AuthOptions> authOptions,
    IHostEnvironment environment,
    ILogger<SettingsService> logger) : ISettingsService
{
    /// <summary>Extensões gravadas por <see cref="Storage.ReceiptImageStorage"/>.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".png", ".webp" };

    /// <summary>Leituras ainda em uso: a imagem é necessária para processar ou conferir.</summary>
    private static readonly ReceiptImportStatus[] ActiveImports =
        [ReceiptImportStatus.Pending, ReceiptImportStatus.Processing, ReceiptImportStatus.AwaitingReview, ReceiptImportStatus.Failed];

    public static readonly string Version =
        typeof(SettingsService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public async Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var counts = new DataCountsResponse(
            await db.Stores.CountAsync(cancellationToken),
            await db.Categories.CountAsync(cancellationToken),
            await db.Products.CountAsync(cancellationToken),
            await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Confirmed, cancellationToken),
            await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Draft, cancellationToken),
            await db.PriceObservations.CountAsync(cancellationToken),
            await db.ReceiptImports.CountAsync(i => ActiveImports.Contains(i.Status), cancellationToken));

        var options = readerOptions.Value;
        var readerInfo = new ReaderSettingsResponse(
            reader.IsAvailable,
            reader.ProviderName,
            reader.ModelName,
            reader.IsAvailable ? options.Effort : null,
            options.TimeoutSeconds,
            reader is UnavailableReceiptReader unavailable ? unavailable.Reason : null);

        // No modo WAL do SQLite, alterações recentes ficam no arquivo "-wal" até o próximo checkpoint.
        var databaseSize = new[] { paths.DatabaseFile, paths.DatabaseFile + "-wal" }
            .Select(file => new FileInfo(file))
            .Where(info => info.Exists)
            .Sum(info => info.Length);
        return new SystemInfoResponse(
            Version,
            environment.EnvironmentName,
            authOptions.Value.Enabled,
            paths.DatabaseFile,
            databaseSize,
            paths.UploadsDirectory,
            counts,
            readerInfo);
    }

    public async Task<ImagesSummaryResponse> GetImagesAsync(CancellationToken cancellationToken)
    {
        var (files, referenced) = await ScanAsync(cancellationToken);
        var orphans = files.Where(f => !referenced.Contains(f.Path)).OrderByDescending(f => f.ModifiedAt).ToList();
        var existing = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ImagesSummaryResponse(
            files.Count,
            files.Sum(f => f.SizeBytes),
            files.Count - orphans.Count,
            referenced.Count(path => !existing.Contains(path)),
            orphans.Sum(f => f.SizeBytes),
            orphans);
    }

    public async Task<DeleteOrphansResponse> DeleteOrphanImagesAsync(CancellationToken cancellationToken)
    {
        // A lista é recalculada aqui: nunca se apaga com base em uma lista enviada pelo cliente.
        var (files, referenced) = await ScanAsync(cancellationToken);
        var deleted = 0;
        long freed = 0;
        foreach (var orphan in files.Where(f => !referenced.Contains(f.Path)))
        {
            try
            {
                File.Delete(FullPath(orphan.Path));
                deleted++;
                freed += orphan.SizeBytes;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Não foi possível excluir a imagem órfã {Path}", orphan.Path);
            }
        }

        RemoveEmptyDirectories(paths.UploadsDirectory);
        logger.LogInformation("Imagens órfãs excluídas: {Count} ({Bytes} bytes)", deleted, freed);
        return new DeleteOrphansResponse(deleted, freed);
    }

    private async Task<(List<ImageFileResponse> Files, HashSet<string> Referenced)> ScanAsync(CancellationToken cancellationToken)
    {
        var receiptImages = await db.Receipts.Where(r => r.ImagePath != null).Select(r => r.ImagePath!).ToListAsync(cancellationToken);
        var importImages = await db.ReceiptImports.Where(i => ActiveImports.Contains(i.Status)).Select(i => i.ImagePath).ToListAsync(cancellationToken);
        var referenced = receiptImages.Concat(importImages).Select(NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var root = Path.GetFullPath(paths.UploadsDirectory);
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => ImageExtensions.Contains(Path.GetExtension(file)))
                .Select(file => new FileInfo(file))
                .Select(info => new ImageFileResponse(
                    NormalizePath(Path.GetRelativePath(root, info.FullName)),
                    info.Length,
                    info.LastWriteTimeUtc))
                .ToList()
            : [];

        return (files, referenced);
    }

    private string FullPath(string relativePath) => Path.GetFullPath(Path.Combine(paths.UploadsDirectory, relativePath));

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
