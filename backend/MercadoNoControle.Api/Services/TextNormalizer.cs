using System.Globalization;
using System.Text;

namespace MercadoNoControle.Api.Services;

public static class TextNormalizer
{
    /// <summary>Remove espaços nas pontas; texto vazio vira nulo.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : CollapseWhitespace(value);

    /// <summary>Remove espaços nas pontas e espaços repetidos internos.</summary>
    public static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Forma canônica de uma descrição para comparação: sem acentos, maiúsculas e
    /// espaços simples. Ex.: " leite  íntegral 1l " → "LEITE INTEGRAL 1L".
    /// </summary>
    public static string NormalizeDescription(string value)
    {
        var decomposed = CollapseWhitespace(value).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
