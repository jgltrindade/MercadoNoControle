using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Api.Services.PriceHistory;

/// <summary>
/// Preço normalizado por kg, litro ou unidade (RN08), calculado somente quando a quantidade
/// comprada pode ser convertida com segurança para a unidade-base do produto:
/// <list type="bullet">
/// <item>Item vendido por peso/volume (unidade da nota KG, G, L, ML): a quantidade já é a medida.</item>
/// <item>Item vendido por embalagem (UN, PCT, CX...): medida = quantidade × conteúdo da embalagem do produto.</item>
/// </list>
/// Unidades incompatíveis (ex.: item em KG de um produto em litros), conteúdo desconhecido e
/// produtos em CX/PCT (conteúdo sem unidade física) não são normalizados.
/// O valor usado é o total pago pelo item (já com desconto).
/// </summary>
public static class PriceNormalizer
{
    /// <summary>Unidade-base e fator de conversão da unidade de medida do produto.</summary>
    public static (NormalizedUnit Unit, decimal Factor)? BaseOf(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.KG => (NormalizedUnit.Kilogram, 1m),
        MeasurementUnit.G => (NormalizedUnit.Kilogram, 0.001m),
        MeasurementUnit.L => (NormalizedUnit.Liter, 1m),
        MeasurementUnit.ML => (NormalizedUnit.Liter, 0.001m),
        MeasurementUnit.UN => (NormalizedUnit.Unit, 1m),
        MeasurementUnit.DZ => (NormalizedUnit.Unit, 12m),
        _ => null
    };

    /// <summary>Unidade de peso/volume impressa na nota (variações comuns), ou nulo se for contagem.</summary>
    public static (NormalizedUnit Unit, decimal Factor)? MeasureOf(string? itemUnit) =>
        itemUnit?.Trim().TrimEnd('.').ToUpperInvariant() switch
        {
            "KG" or "KGS" or "QUILO" => (NormalizedUnit.Kilogram, 1m),
            "G" or "GR" or "GRS" => (NormalizedUnit.Kilogram, 0.001m),
            "L" or "LT" or "LTS" or "LITRO" => (NormalizedUnit.Liter, 1m),
            "ML" => (NormalizedUnit.Liter, 0.001m),
            _ => null
        };

    public static (decimal Price, NormalizedUnit Unit)? Normalize(
        MeasurementUnit productUnit, decimal? packageContent, string? itemUnit, decimal quantity, decimal total)
    {
        if (quantity <= 0 || BaseOf(productUnit) is not { } productBase)
        {
            return null;
        }

        decimal measure;
        if (MeasureOf(itemUnit) is { } itemMeasure)
        {
            if (itemMeasure.Unit != productBase.Unit)
            {
                return null;
            }

            measure = quantity * itemMeasure.Factor;
        }
        else
        {
            if (packageContent is not { } content || content <= 0)
            {
                return null;
            }

            measure = quantity * content * productBase.Factor;
        }

        return measure > 0
            ? (DecimalPrecision.Round(total / measure, DecimalPrecision.NormalizedPrice), productBase.Unit)
            : null;
    }

    /// <summary>Preço efetivamente pago por unidade comprada (total do item ÷ quantidade).</summary>
    public static decimal EffectiveUnitPrice(decimal total, decimal quantity) =>
        quantity > 0 ? DecimalPrecision.Round(total / quantity, DecimalPrecision.UnitPrice) : 0m;
}
