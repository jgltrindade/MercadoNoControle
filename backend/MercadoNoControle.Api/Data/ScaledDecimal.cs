using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MercadoNoControle.Api.Data;

/// <summary>
/// Conversão explícita entre <see cref="decimal"/> e inteiros escalados (Int64),
/// usada para persistir valores monetários e quantidades no SQLite sem perda de precisão.
/// Ex.: escala 2 → R$ 12,34 é gravado como 1234.
/// </summary>
public static class ScaledDecimal
{
    private const int MaxScale = 8;

    private static readonly decimal[] PowersOfTen = BuildPowersOfTen();

    /// <summary>
    /// Converte para inteiro escalado. Lança <see cref="ArgumentException"/> se o valor
    /// possuir mais casas decimais que a escala (nunca arredonda silenciosamente) e
    /// <see cref="OverflowException"/> se não couber em Int64.
    /// </summary>
    public static long ToScaled(decimal value, int scale)
    {
        var scaled = value * Factor(scale);
        if (scaled != decimal.Truncate(scaled))
        {
            throw new ArgumentException(
                $"O valor {value} possui mais de {scale} casas decimais e não pode ser persistido sem perda de precisão. " +
                "Arredonde explicitamente com DecimalPrecision.Round antes de salvar.",
                nameof(value));
        }

        return decimal.ToInt64(scaled);
    }

    /// <summary>Converte um inteiro escalado de volta para decimal com a escala informada.</summary>
    public static decimal FromScaled(long value, int scale)
    {
        var magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        return new decimal((int)(magnitude & 0xFFFFFFFF), (int)(magnitude >> 32), 0, value < 0, (byte)ValidateScale(scale));
    }

    private static decimal Factor(int scale) => PowersOfTen[ValidateScale(scale)];

    private static int ValidateScale(int scale) =>
        scale is >= 0 and <= MaxScale
            ? scale
            : throw new ArgumentOutOfRangeException(nameof(scale), scale, $"A escala deve estar entre 0 e {MaxScale}.");

    private static decimal[] BuildPowersOfTen()
    {
        var powers = new decimal[MaxScale + 1];
        powers[0] = 1m;
        for (var i = 1; i < powers.Length; i++)
        {
            powers[i] = powers[i - 1] * 10m;
        }

        return powers;
    }
}

/// <summary>Value converter do EF Core baseado em <see cref="ScaledDecimal"/>.</summary>
public sealed class ScaledDecimalConverter(int scale) : ValueConverter<decimal, long>(
    value => ScaledDecimal.ToScaled(value, scale),
    stored => ScaledDecimal.FromScaled(stored, scale))
{
    public int Scale { get; } = scale;
}

public static class ScaledDecimalPropertyExtensions
{
    /// <summary>Persiste a propriedade decimal como inteiro escalado com a escala informada.</summary>
    public static PropertyBuilder<decimal> HasScaledDecimal(this PropertyBuilder<decimal> builder, int scale) =>
        builder.HasConversion(new ScaledDecimalConverter(scale));

    /// <summary>Persiste a propriedade decimal anulável como inteiro escalado com a escala informada.</summary>
    public static PropertyBuilder<decimal?> HasScaledDecimal(this PropertyBuilder<decimal?> builder, int scale) =>
        builder.HasConversion(new ScaledDecimalConverter(scale));
}
