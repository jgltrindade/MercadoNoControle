using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Api.Services.Storage;

public sealed class ImageUploadOptions
{
    public const string SectionName = "Uploads";

    /// <summary>Tamanho máximo de uma imagem de nota, em bytes (padrão: 10 MB).</summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;
}

public sealed record StoredImage(string RelativePath, string ContentType, long Size);

public interface IReceiptImageStorage
{
    Task<StoredImage> SaveAsync(Stream content, long length, CancellationToken cancellationToken);

    Stream OpenRead(string relativePath);

    string ContentTypeOf(string relativePath);
}

/// <summary>
/// Armazena imagens de notas no diretório de uploads (fora do banco e do código-fonte).
/// O tipo é identificado pelo conteúdo (assinatura do arquivo), não pelo nome ou cabeçalho
/// enviado. O nome é gerado pelo servidor: <c>receipts/AAAA/MM/{guid}.ext</c>.
/// </summary>
public sealed class ReceiptImageStorage(
    StoragePaths paths,
    IOptions<ImageUploadOptions> options,
    TimeProvider timeProvider) : IReceiptImageStorage
{
    private const int HeaderLength = 12;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    public async Task<StoredImage> SaveAsync(Stream content, long length, CancellationToken cancellationToken)
    {
        var maxSize = options.Value.MaxImageSizeBytes;
        if (length <= 0)
        {
            throw Invalid("O arquivo enviado está vazio.");
        }

        if (length > maxSize)
        {
            throw Invalid($"A imagem excede o tamanho máximo de {maxSize / (1024 * 1024)} MB.");
        }

        var header = new byte[HeaderLength];
        var read = await content.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        var extension = DetectExtension(header.AsSpan(0, read))
                        ?? throw Invalid("Formato de imagem não suportado. Envie JPEG, PNG ou WebP.");

        var now = timeProvider.GetUtcNow();
        var relativePath = $"receipts/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        long written;
        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.WriteAsync(header.AsMemory(0, read), cancellationToken);
            await content.CopyToAsync(file, cancellationToken);
            written = file.Length;
        }

        if (written > maxSize)
        {
            File.Delete(fullPath);
            throw Invalid($"A imagem excede o tamanho máximo de {maxSize / (1024 * 1024)} MB.");
        }

        return new StoredImage(relativePath, ContentTypes[extension], written);
    }

    public Stream OpenRead(string relativePath)
    {
        var fullPath = Resolve(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("A imagem da nota não foi encontrada no armazenamento.");
        }

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public string ContentTypeOf(string relativePath) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(relativePath), "application/octet-stream");

    /// <summary>Resolve o caminho garantindo que permaneça dentro do diretório de uploads.</summary>
    private string Resolve(string relativePath)
    {
        var root = Path.GetFullPath(paths.UploadsDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Caminho de imagem fora do diretório de uploads.");
        }

        return fullPath;
    }

    private static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    private static BusinessRuleException Invalid(string message) =>
        new(message, new Dictionary<string, string[]> { ["file"] = [message] });
}
