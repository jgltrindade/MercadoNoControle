using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Tests.Data;

public class ScaledDecimalTests
{
    [Theory]
    [InlineData("0", 2, 0L)]
    [InlineData("0.01", 2, 1L)]
    [InlineData("12.34", 2, 1234L)]
    [InlineData("12.3", 2, 1230L)]
    [InlineData("12.3400", 2, 1234L)]
    [InlineData("-5.67", 2, -567L)]
    [InlineData("2.999", 4, 29990L)]
    [InlineData("0.3150", 4, 3150L)]
    [InlineData("92233720368547758.07", 2, long.MaxValue)]
    public void ToScaled_converts_exactly(string value, int scale, long expected)
    {
        Assert.Equal(expected, ScaledDecimal.ToScaled(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), scale));
    }

    [Theory]
    [InlineData(1234L, 2, "12.34")]
    [InlineData(-567L, 2, "-5.67")]
    [InlineData(29990L, 4, "2.9990")]
    [InlineData(0L, 2, "0.00")]
    [InlineData(long.MinValue, 2, "-92233720368547758.08")]
    [InlineData(long.MaxValue, 4, "922337203685477.5807")]
    public void FromScaled_restores_value_and_scale(long stored, int scale, string expected)
    {
        var result = ScaledDecimal.FromScaled(stored, scale);

        Assert.Equal(expected, result.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0.001", 2)]
    [InlineData("12.345", 2)]
    [InlineData("1.00001", 4)]
    public void ToScaled_rejects_values_with_more_decimals_than_scale(string value, int scale)
    {
        Assert.Throws<ArgumentException>(() =>
            ScaledDecimal.ToScaled(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), scale));
    }

    [Fact]
    public void ToScaled_rejects_overflow()
    {
        Assert.Throws<OverflowException>(() => ScaledDecimal.ToScaled(decimal.MaxValue / 10m, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Invalid_scale_is_rejected(int scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScaledDecimal.ToScaled(1m, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScaledDecimal.FromScaled(1L, scale));
    }

    [Fact]
    public void Round_trip_preserves_many_values()
    {
        for (var cents = -100_000L; cents <= 100_000L; cents += 7)
        {
            var value = ScaledDecimal.FromScaled(cents, DecimalPrecision.Money);
            Assert.Equal(cents, ScaledDecimal.ToScaled(value, DecimalPrecision.Money));
        }
    }

    [Theory]
    [InlineData("2.345", 2, "2.35")]
    [InlineData("2.355", 2, "2.36")]
    [InlineData("-2.345", 2, "-2.35")]
    [InlineData("2.344", 2, "2.34")]
    [InlineData("10.00005", 4, "10.0001")]
    public void Round_uses_commercial_rounding_away_from_zero(string value, int scale, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(decimal.Parse(expected, culture), DecimalPrecision.Round(decimal.Parse(value, culture), scale));
    }

    [Fact]
    public void Decimal_sum_of_cents_has_no_floating_point_error()
    {
        // 0,10 somado mil vezes deve resultar exatamente em 100,00 (em double daria 99,9999999999986).
        var sum = Enumerable.Repeat(0.10m, 1000).Sum();
        Assert.Equal(100.00m, sum);
        Assert.Equal(10_000L, ScaledDecimal.ToScaled(sum, DecimalPrecision.Money));
    }
}
