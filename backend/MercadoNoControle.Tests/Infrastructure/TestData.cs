using MercadoNoControle.Api.Models;

namespace MercadoNoControle.Tests.Infrastructure;

/// <summary>Construtores de entidades para testes (dados fictícios, nunca usados em produção).</summary>
public static class TestData
{
    public static readonly DateTime FixedNow = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    public static Store Store(string tradeName = "Mercado Teste", string? cnpj = "11222333000181") => new()
    {
        TradeName = tradeName,
        Cnpj = cnpj,
        CreatedAt = FixedNow
    };

    public static Product Product(string name = "Leite Integral 1L", int? categoryId = 4) => new()
    {
        Name = name,
        CategoryId = categoryId,
        Unit = MeasurementUnit.L,
        PackageContent = 1m,
        CreatedAt = FixedNow
    };

    public static Receipt Receipt(Store store, string? accessKey = null, params ReceiptItem[] items)
    {
        var productsTotal = items.Sum(i => i.Total + i.Discount);
        var discount = items.Sum(i => i.Discount);
        return new Receipt
        {
            Store = store,
            AccessKey = accessKey,
            IssuedAt = new DateTime(2026, 11, 15, 14, 30, 0),
            ProductsTotal = productsTotal,
            DiscountTotal = discount,
            Total = productsTotal - discount,
            Status = ReceiptStatus.Confirmed,
            CreatedAt = FixedNow,
            Items = items.ToList()
        };
    }

    public static ReceiptItem Item(int line, string description, decimal quantity, decimal unitPrice, decimal total, decimal discount = 0m, Product? product = null) => new()
    {
        LineNumber = line,
        OriginalDescription = description,
        Quantity = quantity,
        Unit = "UN",
        UnitPrice = unitPrice,
        Discount = discount,
        Total = total,
        Product = product
    };
}
