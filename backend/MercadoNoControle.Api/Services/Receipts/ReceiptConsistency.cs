using System.Globalization;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Validators;

namespace MercadoNoControle.Api.Services.Receipts;

/// <summary>
/// Verificações de consistência dos valores da nota (RN10). Os valores são os impressos na nota;
/// divergências geram alertas — nunca correções automáticas.
/// <list type="bullet">
/// <item>Item: arredondar(quantidade × preço unitário, 2) − desconto = total do item.</item>
/// <item>Valor dos produtos = Σ (total do item + desconto do item).</item>
/// <item>Σ descontos dos itens ≤ descontos da nota (a nota pode ter desconto geral).</item>
/// <item>Total = valor dos produtos − descontos + acréscimos.</item>
/// </list>
/// A tolerância de R$ 0,01 acomoda notas que truncam em vez de arredondar.
/// </summary>
public static class ReceiptConsistency
{
    public const decimal Tolerance = 0.01m;

    public const string NoItems = "NO_ITEMS";
    public const string ItemTotalMismatch = "ITEM_TOTAL_MISMATCH";
    public const string ProductsTotalMismatch = "PRODUCTS_TOTAL_MISMATCH";
    public const string ItemDiscountsExceedTotal = "ITEM_DISCOUNTS_EXCEED_TOTAL";
    public const string TotalMismatch = "TOTAL_MISMATCH";
    public const string AccessKeyCnpjMismatch = "ACCESS_KEY_CNPJ_MISMATCH";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<ReceiptWarning> Check(Receipt receipt, string? storeCnpj)
    {
        var warnings = new List<ReceiptWarning>();
        var items = receipt.Items.OrderBy(i => i.LineNumber).ToList();

        if (items.Count == 0)
        {
            warnings.Add(new ReceiptWarning(NoItems, "A nota não possui itens."));
        }

        foreach (var item in items)
        {
            var expected = DecimalPrecision.Round(item.Quantity * item.UnitPrice, DecimalPrecision.Money) - item.Discount;
            if (Math.Abs(expected - item.Total) > Tolerance)
            {
                warnings.Add(new ReceiptWarning(
                    ItemTotalMismatch,
                    $"Item {item.LineNumber}: quantidade × preço unitário − desconto = {Money(expected)}, mas o total do item é {Money(item.Total)}.",
                    item.LineNumber));
            }
        }

        if (items.Count > 0)
        {
            var itemsGross = items.Sum(i => i.Total + i.Discount);
            if (Math.Abs(itemsGross - receipt.ProductsTotal) > Tolerance)
            {
                warnings.Add(new ReceiptWarning(
                    ProductsTotalMismatch,
                    $"A soma dos itens é {Money(itemsGross)}, mas o valor dos produtos informado é {Money(receipt.ProductsTotal)}."));
            }

            var itemDiscounts = items.Sum(i => i.Discount);
            if (itemDiscounts - receipt.DiscountTotal > Tolerance)
            {
                warnings.Add(new ReceiptWarning(
                    ItemDiscountsExceedTotal,
                    $"Os descontos dos itens somam {Money(itemDiscounts)}, acima do desconto total da nota ({Money(receipt.DiscountTotal)})."));
            }
        }

        var expectedTotal = receipt.ProductsTotal - receipt.DiscountTotal + receipt.AdditionalCharges;
        if (Math.Abs(expectedTotal - receipt.Total) > Tolerance)
        {
            warnings.Add(new ReceiptWarning(
                TotalMismatch,
                $"Produtos − descontos + acréscimos = {Money(expectedTotal)}, mas o total da nota é {Money(receipt.Total)}."));
        }

        if (receipt.AccessKey is { } key && storeCnpj is not null && AccessKeyValidator.CnpjOf(key) != storeCnpj)
        {
            warnings.Add(new ReceiptWarning(
                AccessKeyCnpjMismatch,
                "O CNPJ contido na chave de acesso é diferente do CNPJ do supermercado selecionado."));
        }

        return warnings;
    }

    private static string Money(decimal value) => value.ToString("C", PtBr);
}
