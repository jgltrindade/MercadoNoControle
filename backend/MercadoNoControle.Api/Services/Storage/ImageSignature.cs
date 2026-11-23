namespace MercadoNoControle.Api.Services.Storage;

/// <summary>Identifica imagens suportadas pela assinatura (primeiros bytes), não pelo nome do arquivo.</summary>
public static class ImageSignature
{
    public const int HeaderLength = 12;

    /// <summary>Extensão (".jpg", ".png", ".webp") ou nulo se o conteúdo não for suportado.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
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

    /// <summary>Tipo MIME correspondente ao conteúdo, ou nulo se não suportado.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> content) => DetectExtension(content) switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null
    };
}
