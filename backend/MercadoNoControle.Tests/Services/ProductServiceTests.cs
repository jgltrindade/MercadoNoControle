using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Services;

public class ProductServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public void Dispose() => _database.Dispose();

    private ProductService CreateService() => new(_database.CreateContext(), new FixedTimeProvider());

    private static ProductRequest Request(
        string name,
        string? brand = null,
        int? categoryId = null,
        string? barcode = null,
        MeasurementUnit unit = MeasurementUnit.UN,
        decimal? content = null,
        ProductStatus status = ProductStatus.Active) => new()
    {
        Name = name,
        Brand = brand,
        CategoryId = categoryId,
        Barcode = barcode,
        Unit = unit,
        PackageContent = content,
        Status = status
    };

    [Fact]
    public async Task Create_persists_product_with_category_and_exact_package_content()
    {
        var product = await CreateService().CreateAsync(
            Request(" Leite  Integral 1L ", " Itambé ", 4, "4006381333931", MeasurementUnit.L, 1.0000m), CancellationToken.None);

        Assert.Equal("Leite Integral 1L", product.Name);
        Assert.Equal("Itambé", product.Brand);
        Assert.Equal("Laticínios", product.CategoryName);
        Assert.Equal(MeasurementUnit.L, product.Unit);
        Assert.Equal(1m, product.PackageContent);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Empty(product.Aliases);

        var reloaded = await CreateService().GetAsync(product.Id, CancellationToken.None);
        Assert.Equal(1.0000m, reloaded.PackageContent);
    }

    [Fact]
    public async Task Unknown_category_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(Request("Produto", categoryId: 999), CancellationToken.None));
        Assert.True(exception.Errors.ContainsKey("categoryId"));
    }

    [Fact]
    public async Task Barcode_must_be_unique()
    {
        var first = await CreateService().CreateAsync(Request("Produto A", barcode: "4006381333931"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().CreateAsync(Request("Produto B", barcode: "4006381333931"), CancellationToken.None));

        // O próprio produto pode manter seu código.
        var updated = await CreateService().UpdateAsync(first.Id, Request("Produto A2", barcode: "4006381333931"), CancellationToken.None);
        Assert.Equal("Produto A2", updated.Name);
    }

    [Fact]
    public async Task Same_name_with_different_brand_or_size_are_distinct_products()
    {
        await CreateService().CreateAsync(Request("Leite Integral", "Marca A", content: 1m, unit: MeasurementUnit.L), CancellationToken.None);
        await CreateService().CreateAsync(Request("Leite Integral", "Marca B", content: 1m, unit: MeasurementUnit.L), CancellationToken.None);

        var result = await CreateService().ListAsync(new ProductQuery { Search = "leite" }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task List_filters_by_category_status_and_searches_aliases()
    {
        var milk = await CreateService().CreateAsync(Request("Leite Integral 1L", categoryId: 4), CancellationToken.None);
        await CreateService().CreateAsync(Request("Arroz 5kg", categoryId: 1), CancellationToken.None);
        await CreateService().CreateAsync(Request("Biscoito antigo", categoryId: 1, status: ProductStatus.Inactive), CancellationToken.None);
        await CreateService().AddAliasAsync(milk.Id, new ProductAliasRequest { Description = "LT INT 1000ML" }, CancellationToken.None);

        var byCategory = await CreateService().ListAsync(new ProductQuery { CategoryId = 1 }, CancellationToken.None);
        Assert.Equal(["Arroz 5kg", "Biscoito antigo"], byCategory.Items.Select(p => p.Name));

        var active = await CreateService().ListAsync(new ProductQuery { CategoryId = 1, Status = ProductStatus.Active }, CancellationToken.None);
        Assert.Equal("Arroz 5kg", Assert.Single(active.Items).Name);

        var byAlias = await CreateService().ListAsync(new ProductQuery { Search = "lt int" }, CancellationToken.None);
        var found = Assert.Single(byAlias.Items);
        Assert.Equal(milk.Id, found.Id);
        Assert.Equal(1, found.AliasCount);
        Assert.Equal("Laticínios", found.CategoryName);

        var sorted = await CreateService().ListAsync(new ProductQuery { SortBy = "category" }, CancellationToken.None);
        Assert.Equal("Leite Integral 1L", sorted.Items[0].Name);
    }

    [Fact]
    public async Task Aliases_preserve_original_text_and_reject_equivalent_duplicates()
    {
        var product = await CreateService().CreateAsync(Request("Leite Integral 1L"), CancellationToken.None);

        var alias = await CreateService().AddAliasAsync(product.Id, new ProductAliasRequest { Description = " Leite  Int 1L " }, CancellationToken.None);

        Assert.Equal("Leite Int 1L", alias.Description);
        Assert.True(alias.Confirmed);
        Assert.Equal(AliasSource.Manual, alias.Source);
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().AddAliasAsync(product.Id, new ProductAliasRequest { Description = "LEITE ÍNT 1L" }, CancellationToken.None));
    }

    [Fact]
    public async Task Same_description_may_belong_to_different_products()
    {
        var a = await CreateService().CreateAsync(Request("Leite Marca A"), CancellationToken.None);
        var b = await CreateService().CreateAsync(Request("Leite Marca B"), CancellationToken.None);

        await CreateService().AddAliasAsync(a.Id, new ProductAliasRequest { Description = "LEITE INT" }, CancellationToken.None);
        await CreateService().AddAliasAsync(b.Id, new ProductAliasRequest { Description = "LEITE INT" }, CancellationToken.None);

        Assert.Single((await CreateService().GetAsync(b.Id, CancellationToken.None)).Aliases);
    }

    [Fact]
    public async Task Suggested_alias_can_be_confirmed_and_deleted()
    {
        var product = await CreateService().CreateAsync(Request("Arroz 5kg"), CancellationToken.None);
        int aliasId;
        await using (var context = _database.CreateContext())
        {
            var alias = new ProductAlias
            {
                ProductId = product.Id,
                Description = "ARROZ T1 5KG",
                NormalizedDescription = "ARROZ T1 5KG",
                Confirmed = false,
                Source = AliasSource.AiSuggestion,
                CreatedAt = TestData.FixedNow
            };
            context.ProductAliases.Add(alias);
            await context.SaveChangesAsync();
            aliasId = alias.Id;
        }

        var confirmed = await CreateService().ConfirmAliasAsync(product.Id, aliasId, CancellationToken.None);
        Assert.True(confirmed.Confirmed);
        Assert.Equal(AliasSource.AiSuggestion, confirmed.Source);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().ConfirmAliasAsync(product.Id + 1, aliasId, CancellationToken.None));

        await CreateService().DeleteAliasAsync(product.Id, aliasId, CancellationToken.None);
        Assert.Empty((await CreateService().GetAsync(product.Id, CancellationToken.None)).Aliases);
    }

    [Fact]
    public async Task Delete_removes_unused_product_with_aliases_and_blocks_product_in_use()
    {
        var unused = await CreateService().CreateAsync(Request("Sem uso"), CancellationToken.None);
        await CreateService().AddAliasAsync(unused.Id, new ProductAliasRequest { Description = "SEM USO" }, CancellationToken.None);

        int usedId;
        await using (var context = _database.CreateContext())
        {
            var product = TestData.Product("Usado");
            context.Receipts.Add(TestData.Receipt(TestData.Store(), items: [TestData.Item(1, "USADO", 1m, 1.0000m, 1.00m, product: product)]));
            await context.SaveChangesAsync();
            usedId = product.Id;
        }

        await CreateService().DeleteAsync(unused.Id, CancellationToken.None);
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetAsync(unused.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().DeleteAsync(usedId, CancellationToken.None));
    }
}
