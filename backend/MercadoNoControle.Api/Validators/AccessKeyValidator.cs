using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.Validators;

/// <summary>
/// Chave de acesso de NF-e/NFC-e (44 caracteres): cUF(2) AAMM(4) CNPJ(14) modelo(2) série(3)
/// número(9) tpEmis(1) código(8) DV(1). O trecho do CNPJ aceita o formato alfanumérico;
/// o dígito verificador é calculado por módulo 11 (pesos 2 a 9 da direita para a esquerda),
/// com o valor de cada caractere = código ASCII − 48, como no CNPJ alfanumérico.
/// </summary>
public static class AccessKeyValidator
{
    public const int Length = 44;

    /// <summary>Remove espaços e pontuação e converte para maiúsculas.</summary>
    public static string Normalize(string value) =>
        new(value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = Normalize(value);
        if (key.Length != Length
            || !key[..6].All(char.IsAsciiDigit)
            || !key[6..20].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))
            || !key[20..].All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = Length - 2; i >= 0; i--)
        {
            var weight = 2 + (Length - 2 - i) % 8;
            sum += (key[i] - '0') * weight;
        }

        var remainder = sum % 11;
        var checkDigit = remainder < 2 ? 0 : 11 - remainder;
        return key[^1] - '0' == checkDigit;
    }

    /// <summary>CNPJ do emitente contido na chave (posições 7 a 20).</summary>
    public static string CnpjOf(string normalizedKey) => normalizedKey.Substring(6, 14);
}

/// <summary>Valida uma chave de acesso opcional (nula/vazia é aceita).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AccessKeyAttribute : ValidationAttribute
{
    public AccessKeyAttribute() : base("Chave de acesso inválida (44 caracteres com dígito verificador).")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || AccessKeyValidator.IsValid(text)));
}
