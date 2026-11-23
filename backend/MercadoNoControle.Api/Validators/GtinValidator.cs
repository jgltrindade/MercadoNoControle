using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.Validators;

/// <summary>Validação de códigos de barras GTIN-8, GTIN-12 (UPC), GTIN-13 (EAN) e GTIN-14.</summary>
public static class GtinValidator
{
    private static readonly int[] ValidLengths = [8, 12, 13, 14];

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var code = value.Trim();
        if (!ValidLengths.Contains(code.Length) || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        // Pesos 3 e 1 alternados a partir do dígito imediatamente à esquerda do verificador.
        var sum = 0;
        for (var i = code.Length - 2; i >= 0; i--)
        {
            var weight = (code.Length - 2 - i) % 2 == 0 ? 3 : 1;
            sum += (code[i] - '0') * weight;
        }

        var checkDigit = (10 - sum % 10) % 10;
        return code[^1] - '0' == checkDigit;
    }
}

/// <summary>Valida um código de barras GTIN opcional (nulo/vazio é aceito).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class GtinAttribute : ValidationAttribute
{
    public GtinAttribute() : base("Código de barras inválido (esperado GTIN/EAN com 8, 12, 13 ou 14 dígitos).")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || GtinValidator.IsValid(text)));
}
