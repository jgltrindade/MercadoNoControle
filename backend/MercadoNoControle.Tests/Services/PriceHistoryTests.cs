using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.DTOs.PriceHistory;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.PriceHistory;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Services.Storage;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Tests.Services;

public class PriceNormalizerTests
{
    [Theory]
    // Produto embalado comprado por unidade: total ÷ (quantidade × conteúdo).
    [InlineData(MeasurementUnit.L, "1", "UN", "2", "10.98", "5.4900", NormalizedUnit.Liter)]
    [InlineData(MeasurementUnit.KG, "5", "UN", "1", "27.90", "5.5800", NormalizedUnit.Kilogram)]
    [InlineData(MeasurementUnit.G, "500", "PCT", "2", "15.00", "15.0000", NormalizedUnit.Kilogram)]
    [InlineData(MeasurementUnit.ML, "350", "UN", "12", "42.00", "10.0000", NormalizedUnit.Liter)]
    [InlineData(MeasurementUnit.DZ, "1", "UN", "1", "12.00", "1.0000", NormalizedUnit.Unit)]
    [InlineData(MeasurementUnit.UN, "3", null, "1", "9.00", "3.0000", NormalizedUnit.Unit)]
    // Produto a granel: a quantidade da nota já é a medida.
    [InlineData(MeasurementUnit.KG, null, "KG", "1.235", "8.63", "6.9879", NormalizedUnit.Kilogram)]
    [InlineData(MeasurementUnit.KG, "1", "kg.", "0.500", "3.50", "7.0000", NormalizedUnit.Kilogram)]
    [InlineData(MeasurementUnit.G, null, "GR", "250", "5.00", "20.0000", NormalizedUnit.Kilogram)]
    [InlineData(MeasurementUnit.L, null, "LT", "2", "9.00", "4.5000", NormalizedUnit.Liter)]
    public void Normalizes_when_quantity_and_unit_are_known(
        MeasurementUnit unit, string? content, string? itemUnit, string quantity, string total, string expected, NormalizedUnit expectedUnit)
    {
        var result = PriceNormalizer.Normalize(unit, D(content), itemUnit, D(quantity)!.Value, D(total)!.Value);

        Assert.NotNull(result);
        Assert.Equal(D(expected), result.Value.Price);
        Assert.Equal(expectedUnit, result.Value.Unit);
    }

    [Theory]
    [InlineData(MeasurementUnit.L, null, "UN", "1")]      // conteúdo desconhecido
    [InlineData(MeasurementUnit.UN, null, "UN", "1")]     // conteúdo desconhecido
    [InlineData(MeasurementUnit.CX, "12", "UN", "1")]     // caixa: conteúdo sem unidade física
    [InlineData(MeasurementUnit.PCT, "1", "UN", "1")]
    [InlineData(MeasurementUnit.L, "1", "KG", "1")]       // unidades incompatíveis
    [InlineData(MeasurementUnit.UN, "1", "KG", "1")]
    [InlineData(MeasurementUnit.KG, "1", "UN", "0")]      // quantidade inválida
    public void Does_not_normalize_without_compatible_known_measures(MeasurementUnit unit, string? content, string? itemUnit, string quantity)
    {
        Assert.Null(PriceNormalizer.Normalize(unit, D(content), itemUnit, D(quantity)!.Value, 10m));
    }

    [Fact]
    public void Effective_unit_price_considers_the_total_paid()
    {
        Assert.Equal(4.9900m, PriceNormalizer.EffectiveUnitPrice(9.98m, 2m));
        Assert.Equal(3.3333m, PriceNormalizer.EffectiveUnitPrice(10.00m, 3m));
        Assert.Equal(0m, PriceNormalizer.EffectiveUnitPrice(10m, 0m));
    }

    private static decimal? D(string? value) =>
        value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

public class PriceHistoryServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly int _storeA;
    private readonly int _storeB;
    private readonly int _milk;
    private readonly int _otherMilk;
    private readonly int _juice;
    private readonly int _rice;

    public PriceHistoryServiceTests()
    {
        using var context = _database.CreateContext();
        var a = TestData.Store("Mercado A", "11222333000181");
        var b = TestData.Store("Mercado B", "12345678000195");
        var milk = TestData.Product("Leite Integral 1L"); // Laticínios, L, 1
        var otherMilk = TestData.Product("Leite Integral Marca B 1L");
        var juice = TestData.Product("Suco 1L", categoryId: 5);
        var rice = new Product { Name = "Arroz 5kg", CategoryId = 1, Unit = MeasurementUnit.KG, PackageContent = 5m, CreatedAt = TestData.FixedNow };
        context.AddRange(a, b, milk, otherMilk, juice, rice);
        context.SaveChanges();
        (_storeA, _storeB, _milk, _otherMilk, _juice, _rice) = (a.Id, b.Id, milk.Id, otherMilk.Id, juice.Id, rice.Id);
    }

    public void Dispose() => _database.Dispose();

    private PriceHistoryService CreateService()
    {
        var context = _database.CreateContext();
        return new PriceHistoryService(context, new PriceObservationRecorder(context), new FixedTimeProvider());
    }

    private ReceiptService CreateReceiptService()
    {
        var context = _database.CreateContext();
        var storage = new ReceiptImageStorage(new StoragePaths("unused.db", Path.GetTempPath()), Options.Create(new ImageUploadOptions()), new FixedTimeProvider());
        return new ReceiptService(context, new PriceObservationRecorder(context), storage, new FixedTimeProvider());
    }

    private async Task<int> BuyAsync(int storeId, DateTime date, params (string Description, int? ProductId, decimal Quantity, string? Unit, decimal Total)[] items)
    {
        var total = items.Sum(i => i.Total);
        var request = new ReceiptRequest
        {
            StoreId = storeId,
            IssuedAt = date,
            ProductsTotal = total,
            Total = total,
            Items = items.Select(i => new ReceiptItemRequest
            {
                Description = i.Description,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Unit = i.Unit,
                UnitPrice = DecimalPrecision.Round(i.Total / i.Quantity, 4),
                Total = i.Total
            }).ToList()
        };
        var draft = await CreateReceiptService().CreateAsync(request, CancellationToken.None);
        await CreateReceiptService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, CancellationToken.None);
        return draft.Id;
    }

    [Fact]
    public async Task Confirmed_purchases_record_the_normalized_price()
    {
        await BuyAsync(_storeA, new DateTime(2026, 11, 1),
            ("LEITE INT 1L", _milk, 2m, "UN", 10.98m),
            ("ARROZ 5KG", _rice, 1m, "UN", 27.90m));

        await using var context = _database.CreateContext();
        var observations = await context.PriceObservations.ToDictionaryAsync(o => o.ProductId);
        Assert.Equal(5.4900m, observations[_milk].NormalizedPrice);
        Assert.Equal(NormalizedUnit.Liter, observations[_milk].NormalizedUnit);
        Assert.Equal(5.5800m, observations[_rice].NormalizedPrice);
        Assert.Equal(NormalizedUnit.Kilogram, observations[_rice].NormalizedUnit);
    }

    [Fact]
    public async Task Changing_the_product_package_recalculates_its_history_only()
    {
        await BuyAsync(_storeA, new DateTime(2026, 11, 1),
            ("LEITE INT 1L", _milk, 2m, "UN", 10.98m),
            ("SUCO 1L", _juice, 1m, "UN", 8.00m));

        await using (var context = _database.CreateContext())
        {
            await new ProductService(context, new FixedTimeProvider()).UpdateAsync(_milk, new ProductRequest
            {
                Name = "Leite Integral 2L", CategoryId = 4, Unit = MeasurementUnit.L, PackageContent = 2m
            }, CancellationToken.None);
        }

        await using (var context = _database.CreateContext())
        {
            var observations = await context.PriceObservations.ToDictionaryAsync(o => o.ProductId);
            Assert.Equal(2.7450m, observations[_milk].NormalizedPrice);
            Assert.Equal(8.0000m, observations[_juice].NormalizedPrice);
        }
    }

    [Fact]
    public async Task Recalculation_fills_observations_recorded_without_normalized_price()
    {
        await BuyAsync(_storeA, new DateTime(2026, 11, 1), ("LEITE INT 1L", _milk, 2m, "UN", 10.98m));
        await using (var context = _database.CreateContext())
        {
            await context.PriceObservations.ExecuteUpdateAsync(s => s
                .SetProperty(o => o.NormalizedPrice, (decimal?)null)
                .SetProperty(o => o.NormalizedUnit, (NormalizedUnit?)null));
        }

        await using (var context = _database.CreateContext())
        {
            var changed = await new PriceObservationRecorder(context).RecalculateAsync(null, CancellationToken.None);
            Assert.Equal(1, changed);
            Assert.Equal(0, await new PriceObservationRecorder(context).RecalculateAsync(null, CancellationToken.None));
        }

        await using (var verify = _database.CreateContext())
        {
            Assert.Equal(5.4900m, (await verify.PriceObservations.SingleAsync()).NormalizedPrice);
        }
    }

    [Fact]
    public async Task History_summarizes_prices_exactly_in_decimal()
    {
        await BuyAsync(_storeA, new DateTime(2026, 9, 10), ("LEITE INT 1L", _milk, 1m, "UN", 4.99m));
        await BuyAsync(_storeB, new DateTime(2026, 10, 5), ("LEITE INTEGRAL", _milk, 2m, "UN", 11.00m));
        await BuyAsync(_storeA, new DateTime(2026, 11, 15), ("LEITE INT 1L", _milk, 3m, "UN", 16.47m));

        var history = await CreateService().GetProductPricesAsync(_milk, new PriceHistoryQuery(), CancellationToken.None);

        Assert.Equal(NormalizedUnit.Liter, history.NormalizedUnit);
        Assert.Equal([4.99m, 5.50m, 5.49m], history.Points.Select(p => p.EffectiveUnitPrice));
        Assert.Equal(["Mercado A", "Mercado B", "Mercado A"], history.Points.Select(p => p.StoreName));
        var summary = history.Summary;
        Assert.Equal(3, summary.Count);
        Assert.Equal(new PriceReference(5.49m, new DateTime(2026, 11, 15), "Mercado A"), summary.Last);
        Assert.Equal(4.99m, summary.Lowest!.Price);
        Assert.Equal(new PriceReference(5.50m, new DateTime(2026, 10, 5), "Mercado B"), summary.Highest);
        Assert.Equal(5.3267m, summary.Average);               // (4,99 + 5,50 + 5,49) ÷ 3
        Assert.Equal(10.02m, summary.VariationPercent);       // 4,99 → 5,49
        Assert.Equal(NormalizedUnit.Liter, summary.NormalizedUnit);
        Assert.Equal(5.4900m, summary.LastNormalized);
        Assert.Equal(4.9900m, summary.LowestNormalized);
    }

    [Fact]
    public async Task History_filters_by_period_and_store()
    {
        await BuyAsync(_storeA, new DateTime(2026, 9, 10), ("LEITE INT 1L", _milk, 1m, "UN", 4.99m));
        await BuyAsync(_storeB, new DateTime(2026, 10, 5), ("LEITE INT 1L", _milk, 1m, "UN", 5.50m));
        await BuyAsync(_storeA, new DateTime(2026, 11, 15, 20, 0, 0), ("LEITE INT 1L", _milk, 1m, "UN", 5.49m));

        var autumn = await CreateService().GetProductPricesAsync(_milk,
            new PriceHistoryQuery { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 11, 15) }, CancellationToken.None);
        Assert.Equal(2, autumn.Summary.Count);

        var storeA = await CreateService().GetProductPricesAsync(_milk, new PriceHistoryQuery { StoreId = _storeA }, CancellationToken.None);
        Assert.All(storeA.Points, p => Assert.Equal(_storeA, p.StoreId));

        var empty = await CreateService().GetProductPricesAsync(_rice, new PriceHistoryQuery(), CancellationToken.None);
        Assert.Equal(0, empty.Summary.Count);
        Assert.Null(empty.Summary.Average);
        Assert.Null(empty.Summary.VariationPercent);

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().GetProductPricesAsync(_milk,
            new PriceHistoryQuery { From = new DateOnly(2026, 12, 1), To = new DateOnly(2026, 11, 1) }, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetProductPricesAsync(999, new PriceHistoryQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Comparison_ranks_stores_and_lists_comparable_products_of_the_same_category()
    {
        await BuyAsync(_storeA, new DateTime(2026, 11, 1), ("LEITE INT 1L", _milk, 1m, "UN", 5.49m));
        await BuyAsync(_storeA, new DateTime(2026, 11, 10), ("LEITE INT 1L", _milk, 1m, "UN", 5.99m));
        await BuyAsync(_storeB, new DateTime(2026, 11, 5), ("LEITE INT 1L", _milk, 2m, "UN", 10.00m),
            ("LEITE MARCA B", _otherMilk, 1m, "UN", 4.79m),
            ("SUCO 1L", _juice, 1m, "UN", 7.00m));

        var comparison = await CreateService().GetComparisonAsync(_milk, new PriceHistoryQuery(), CancellationToken.None);

        Assert.Equal(["Mercado B", "Mercado A"], comparison.Stores.Select(s => s.StoreName));
        var cheapest = comparison.Stores[0];
        Assert.Equal(5.00m, cheapest.Last.Price);
        Assert.Equal(0m, cheapest.DifferenceToCheapestPercent);
        var storeA = comparison.Stores[1];
        Assert.Equal(2, storeA.Count);
        Assert.Equal(5.99m, storeA.Last.Price);
        Assert.Equal(5.49m, storeA.Lowest);
        Assert.Equal(5.74m, storeA.Average);
        Assert.Equal(19.80m, storeA.DifferenceToCheapestPercent);

        // Mesma categoria (Laticínios) e mesma unidade-base; o suco (Bebidas) não entra.
        var alternative = Assert.Single(comparison.Alternatives);
        Assert.Equal(_otherMilk, alternative.ProductId);
        Assert.Equal(4.7900m, alternative.LastNormalized);
    }

    [Fact]
    public async Task Unassigned_items_are_grouped_by_normalized_description_and_suggested_when_unambiguous()
    {
        await BuyAsync(_storeA, new DateTime(2026, 11, 1), ("Pão Francês", null, 0.5m, "KG", 7.50m));
        await BuyAsync(_storeB, new DateTime(2026, 11, 3), ("PAO FRANCES", null, 0.4m, "KG", 6.00m), ("LEITE INT 1L", null, 1m, "UN", 5.49m));
        await using (var context = _database.CreateContext())
        {
            context.ProductAliases.Add(new ProductAlias
            {
                ProductId = _milk, Description = "LEITE INT 1L", NormalizedDescription = "LEITE INT 1L", Confirmed = true, CreatedAt = TestData.FixedNow
            });
            await context.SaveChangesAsync();
        }

        // Rascunhos não entram.
        await CreateReceiptService().CreateAsync(new ReceiptRequest
        {
            StoreId = _storeA, IssuedAt = new DateTime(2026, 11, 4), ProductsTotal = 1m, Total = 1m,
            Items = [new ReceiptItemRequest { Description = "RASCUNHO", Quantity = 1m, UnitPrice = 1m, Total = 1m }]
        }, CancellationToken.None);

        var groups = await CreateService().ListUnassignedAsync(CancellationToken.None);

        Assert.Equal(["PAO FRANCES", "LEITE INT 1L"], groups.Select(g => g.NormalizedDescription));
        var bread = groups[0];
        Assert.Equal(2, bread.Occurrences);
        Assert.Equal(13.50m, bread.TotalSpent);
        Assert.Equal("PAO FRANCES", bread.Description); // descrição da compra mais recente
        Assert.Equal(["Mercado A", "Mercado B"], bread.Stores);
        Assert.Null(bread.SuggestedProductId);
        Assert.Equal(_milk, groups[1].SuggestedProductId);
    }

    [Fact]
    public async Task Assigning_items_creates_history_and_alias_for_every_matching_item()
    {
        int bread;
        await using (var context = _database.CreateContext())
        {
            var product = new Product { Name = "Pão Francês", CategoryId = 9, Unit = MeasurementUnit.KG, CreatedAt = TestData.FixedNow };
            context.Products.Add(product);
            await context.SaveChangesAsync();
            bread = product.Id;
        }

        await BuyAsync(_storeA, new DateTime(2026, 11, 1), ("Pão Francês", null, 0.5m, "KG", 7.50m));
        await BuyAsync(_storeB, new DateTime(2026, 11, 3), ("PAO FRANCES", null, 0.4m, "KG", 6.00m));

        var result = await CreateService().AssignAsync(new AssignItemsRequest { Description = "pão francês", ProductId = bread }, CancellationToken.None);

        Assert.Equal(2, result.UpdatedItems);
        await using (var context = _database.CreateContext())
        {
            var observations = await context.PriceObservations.Where(o => o.ProductId == bread).OrderBy(o => o.ObservedAt).ToListAsync();
            Assert.Equal([15.0000m, 15.0000m], observations.Select(o => o.NormalizedPrice));
            var alias = await context.ProductAliases.SingleAsync(a => a.ProductId == bread);
            Assert.Equal("PAO FRANCES", alias.NormalizedDescription);
            Assert.True(alias.Confirmed);
            Assert.Equal(AliasSource.Receipt, alias.Source);
        }

        Assert.Empty(await CreateService().ListUnassignedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().AssignAsync(new AssignItemsRequest { Description = "PAO FRANCES", ProductId = bread }, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().AssignAsync(new AssignItemsRequest { Description = "X", ProductId = 999 }, CancellationToken.None));
    }
}
