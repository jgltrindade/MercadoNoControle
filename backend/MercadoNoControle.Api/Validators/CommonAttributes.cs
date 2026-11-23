using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.Validators;

/// <summary>Valida uma UF brasileira (sigla com duas letras). Nulo/vazio é aceito.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class BrazilianStateAttribute : ValidationAttribute
{
    public static readonly IReadOnlySet<string> States = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    public BrazilianStateAttribute() : base("UF inválida.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || States.Contains(text.Trim())));
}

/// <summary>
/// Garante que um decimal não tenha mais casas que a escala de persistência,
/// evitando arredondamentos implícitos (ver DecimalPrecision).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DecimalPlacesAttribute(int maxDecimalPlaces) : ValidationAttribute(
    $"Informe no máximo {maxDecimalPlaces} casas decimais.")
{
    public int MaxDecimalPlaces { get; } = maxDecimalPlaces;

    public override bool IsValid(object? value) =>
        value is not decimal number || Math.Round(number, MaxDecimalPlaces) == number;
}
