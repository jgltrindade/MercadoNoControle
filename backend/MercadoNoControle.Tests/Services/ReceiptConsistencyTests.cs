using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Validators;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Services;

public class AccessKeyValidatorTests
{
    private const string ValidKey = "35261111222333000181650010000123451000012345";

    [Theory]
    [InlineData(ValidKey)]
    [InlineData("3526 1111 2223 3300 0181 6500 1000 0123 4510 0001 2345")]
    public void Accepts_valid_access_key(string key) => Assert.True(AccessKeyValidator.IsValid(key));

    [Theory]
    [InlineData("35261111222333000181650010000123451000012344")] // DV errado
    [InlineData("3526111122233300018165001000012345100001234")]  // 43 dígitos
    [InlineData("3526111122233300018165001000012345100001234A")] // letra fora do trecho do CNPJ
    [InlineData("")]
    public void Rejects_invalid_access_key(string key) => Assert.False(AccessKeyValidator.IsValid(key));

    [Fact]
    public void Extracts_issuer_cnpj() =>
        Assert.Equal("11222333000181", AccessKeyValidator.CnpjOf(ValidKey));
}

public class ReceiptConsistencyTests
{
    private static Receipt Receipt(decimal productsTotal, decimal discount, decimal charges, decimal total, params ReceiptItem[] items) => new()
    {
        ProductsTotal = productsTotal,
        DiscountTotal = discount,
        AdditionalCharges = charges,
        Total = total,
        Items = items.ToList()
    };

    private static IEnumerable<string> Codes(Receipt receipt, string? cnpj = null) =>
        ReceiptConsistency.Check(receipt, cnpj).Select(w => w.Code);

    [Fact]
    public void Consistent_receipt_has_no_warnings()
    {
        var receipt = Receipt(37.53m, 1.00m, 0m, 36.53m,
            TestData.Item(1, "BANANA KG", 1.235m, 6.99m, 8.63m),
            TestData.Item(2, "ARROZ 5KG", 1m, 28.90m, 27.90m, discount: 1.00m));

        Assert.Empty(ReceiptConsistency.Check(receipt, null));
    }

    [Fact]
    public void Tolerates_one_cent_from_truncation()
    {
        // 1,235 × 6,99 = 8,63265 → arredondado 8,63; uma nota que trunca imprimiria 8,63 também,
        // mas 0,333 × 3,00 = 0,999 → 1,00 arredondado; truncado 0,99.
        var receipt = Receipt(0.99m, 0m, 0m, 0.99m, TestData.Item(1, "QUEIJO KG", 0.333m, 3.00m, 0.99m));

        Assert.Empty(ReceiptConsistency.Check(receipt, null));
    }

    [Fact]
    public void Flags_item_total_mismatch_with_line_number()
    {
        var receipt = Receipt(12.00m, 0m, 0m, 12.00m,
            TestData.Item(1, "OK", 1m, 2.00m, 2.00m),
            TestData.Item(2, "ERRADO", 2m, 4.00m, 10.00m));

        var warning = Assert.Single(ReceiptConsistency.Check(receipt, null));
        Assert.Equal(ReceiptConsistency.ItemTotalMismatch, warning.Code);
        Assert.Equal(2, warning.LineNumber);
        Assert.Contains("R$ 8,00", warning.Message);
    }

    [Fact]
    public void Flags_products_total_and_total_mismatches()
    {
        var receipt = Receipt(50.00m, 0m, 0m, 45.00m, TestData.Item(1, "A", 1m, 40.00m, 40.00m));

        Assert.Equal(
            [ReceiptConsistency.ProductsTotalMismatch, ReceiptConsistency.TotalMismatch],
            Codes(receipt));
    }

    [Fact]
    public void Accepts_general_discount_and_additional_charges()
    {
        // Produtos 100,00 − desconto geral 5,00 + acréscimo 2,50 = 97,50.
        var receipt = Receipt(100.00m, 5.00m, 2.50m, 97.50m, TestData.Item(1, "A", 1m, 100.00m, 100.00m));

        Assert.Empty(ReceiptConsistency.Check(receipt, null));
    }

    [Fact]
    public void Flags_item_discounts_above_receipt_discount()
    {
        var receipt = Receipt(10.00m, 0m, 0m, 10.00m, TestData.Item(1, "A", 1m, 10.00m, 8.00m, discount: 2.00m));

        Assert.Contains(ReceiptConsistency.ItemDiscountsExceedTotal, Codes(receipt));
    }

    [Fact]
    public void Flags_receipt_without_items_and_cnpj_mismatch_in_access_key()
    {
        var receipt = Receipt(0m, 0m, 0m, 0m);
        receipt.AccessKey = "35261111222333000181650010000123451000012345";

        Assert.Equal([ReceiptConsistency.NoItems, ReceiptConsistency.AccessKeyCnpjMismatch], Codes(receipt, "12345678000195"));
        Assert.DoesNotContain(ReceiptConsistency.AccessKeyCnpjMismatch, Codes(receipt, "11222333000181"));
    }
}
