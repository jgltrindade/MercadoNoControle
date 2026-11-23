using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.Validators;

/// <summary>
/// Validação e normalização de CNPJ, incluindo o formato alfanumérico
/// (IN RFB nº 2.229/2024, vigente desde julho de 2026): 12 caracteres [0-9A-Z]
/// seguidos de 2 dígitos verificadores numéricos. Para CNPJs só numéricos, o
/// cálculo é idêntico ao tradicional.
/// </summary>
public static class CnpjValidator
{
    public const int Length = 14;

    private static readonly int[] FirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>Remove pontuação (. / -) e espaços e converte para maiúsculas.</summary>
    public static string Normalize(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c) && c is not ('.' or '/' or '-')).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Verifica formato e dígitos verificadores. Aceita entrada com ou sem máscara.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cnpj = Normalize(value);
        if (cnpj.Length != Length
            || !cnpj[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))
            || !cnpj[12..].All(char.IsAsciiDigit)
            || cnpj.All(c => c == cnpj[0]))
        {
            return false;
        }

        var first = CheckDigit(cnpj.AsSpan(0, 12), FirstWeights);
        var second = CheckDigit(cnpj.AsSpan(0, 13), SecondWeights);
        return cnpj[12] - '0' == first && cnpj[13] - '0' == second;
    }

    private static int CheckDigit(ReadOnlySpan<char> characters, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < characters.Length; i++)
        {
            // Valor de cada caractere = código ASCII − 48 ('0' = 0, 'A' = 17, ...).
            sum += (characters[i] - '0') * weights[i];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

/// <summary>Valida um CNPJ opcional (nulo/vazio é aceito).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class CnpjAttribute : ValidationAttribute
{
    public CnpjAttribute() : base("CNPJ inválido.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || CnpjValidator.IsValid(text)));
}
