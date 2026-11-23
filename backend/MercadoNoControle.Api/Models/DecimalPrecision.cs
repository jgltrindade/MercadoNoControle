namespace MercadoNoControle.Api.Models;

/// <summary>
/// Escalas (casas decimais) definidas pelo domínio para cada tipo de valor numérico.
/// Os valores são persistidos no SQLite como inteiros escalados (ex.: centavos),
/// nunca como ponto flutuante. Ver "Decisões técnicas" em docs/STATUS_DESENVOLVIMENTO.md.
/// </summary>
public static class DecimalPrecision
{
    /// <summary>Valores monetários totais, descontos e acréscimos: centavos.</summary>
    public const int Money = 2;

    /// <summary>Preço unitário: até 4 casas (ex.: R$ 12,9900 por kg).</summary>
    public const int UnitPrice = 4;

    /// <summary>Quantidades: até 4 casas (ex.: 0,3150 kg).</summary>
    public const int Quantity = 4;

    /// <summary>Preço normalizado (por kg, litro ou unidade): até 4 casas.</summary>
    public const int NormalizedPrice = 4;

    /// <summary>Conteúdo da embalagem (ex.: 0,5 kg; 350 ml): até 4 casas.</summary>
    public const int PackageContent = 4;

    /// <summary>
    /// Arredonda um valor para a escala informada usando arredondamento comercial
    /// (meio para longe do zero). Deve ser usado explicitamente pelas regras de negócio
    /// antes de persistir valores calculados.
    /// </summary>
    public static decimal Round(decimal value, int scale) =>
        Math.Round(value, scale, MidpointRounding.AwayFromZero);
}
