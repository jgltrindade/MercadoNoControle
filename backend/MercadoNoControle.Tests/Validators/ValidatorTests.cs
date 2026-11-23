using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Validators;

namespace MercadoNoControle.Tests.Validators;

public class CnpjValidatorTests
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData(" 11.222.333/0001-81 ")]
    [InlineData("12ABC34501DE35")]          // CNPJ alfanumérico (exemplo oficial da Receita Federal)
    [InlineData("12.abc.345/01de-35")]
    public void Accepts_valid_cnpj(string cnpj) => Assert.True(CnpjValidator.IsValid(cnpj));

    [Theory]
    [InlineData("11222333000182")]          // dígito verificador errado
    [InlineData("11222333000191")]
    [InlineData("12ABC34501DE36")]
    [InlineData("00000000000000")]          // todos iguais
    [InlineData("1122233300018")]           // curto
    [InlineData("112223330001811")]         // longo
    [InlineData("12ABC34501DEAB")]          // verificadores devem ser numéricos
    [InlineData("12ABC34501D#35")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_cnpj(string? cnpj) => Assert.False(CnpjValidator.IsValid(cnpj));

    [Fact]
    public void Normalize_removes_mask_and_uppercases() =>
        Assert.Equal("12ABC34501DE35", CnpjValidator.Normalize("12.abc.345/01de-35"));

    [Fact]
    public void Attribute_accepts_empty_values()
    {
        var attribute = new CnpjAttribute();
        Assert.True(attribute.IsValid(null));
        Assert.True(attribute.IsValid(""));
        Assert.False(attribute.IsValid("123"));
    }
}

public class GtinValidatorTests
{
    [Theory]
    [InlineData("96385074")]       // GTIN-8
    [InlineData("036000291452")]   // GTIN-12 (UPC-A)
    [InlineData("4006381333931")]  // GTIN-13 (EAN)
    [InlineData("14006381333938")] // GTIN-14
    public void Accepts_valid_gtin(string code) => Assert.True(GtinValidator.IsValid(code));

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("400638133393")]
    [InlineData("40063813339A1")]
    [InlineData("123456789")]
    [InlineData("")]
    public void Rejects_invalid_gtin(string code) => Assert.False(GtinValidator.IsValid(code));
}

public class CommonAttributeTests
{
    [Theory]
    [InlineData("SP", true)]
    [InlineData("rj", true)]
    [InlineData("XX", false)]
    [InlineData("SAO", false)]
    [InlineData(null, true)]
    public void BrazilianState_validates_uf(string? value, bool expected) =>
        Assert.Equal(expected, new BrazilianStateAttribute().IsValid(value));

    [Theory]
    [InlineData("1.5", true)]
    [InlineData("0.0001", true)]
    [InlineData("0.00001", false)]
    [InlineData("2.12345", false)]
    public void DecimalPlaces_limits_scale(string value, bool expected) =>
        Assert.Equal(expected, new DecimalPlacesAttribute(4).IsValid(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Required_rejects_whitespace_only_strings() =>
        Assert.False(new RequiredAttribute().IsValid("   "));
}

public class TextNormalizerTests
{
    [Theory]
    [InlineData(" leite  íntegral 1l ", "LEITE INTEGRAL 1L")]
    [InlineData("Pão Francês", "PAO FRANCES")]
    [InlineData("AÇÚCAR\tREFINADO", "ACUCAR REFINADO")]
    [InlineData("LT INT 1000ML", "LT INT 1000ML")]
    public void NormalizeDescription_removes_accents_case_and_extra_spaces(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeDescription(input));

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Rua  das Flores ", "Rua das Flores")]
    public void Clean_trims_and_converts_blank_to_null(string? input, string? expected) =>
        Assert.Equal(expected, TextNormalizer.Clean(input));
}
