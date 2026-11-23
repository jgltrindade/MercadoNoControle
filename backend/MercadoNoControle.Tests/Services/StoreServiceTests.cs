using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Services;

public class StoreServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public void Dispose() => _database.Dispose();

    private StoreService CreateService() => new(_database.CreateContext(), new FixedTimeProvider());

    private static StoreRequest Request(string tradeName, string? cnpj = null, string? city = null, string? state = null) => new()
    {
        TradeName = tradeName,
        Cnpj = cnpj,
        City = city,
        State = state
    };

    [Fact]
    public async Task Create_normalizes_fields()
    {
        var store = await CreateService().CreateAsync(new StoreRequest
        {
            TradeName = "  Super   Bom ",
            LegalName = " ",
            Cnpj = "11.222.333/0001-81",
            City = " Campinas ",
            State = "sp",
            Notes = "  aberto 24h  "
        }, CancellationToken.None);

        Assert.Equal("Super Bom", store.TradeName);
        Assert.Null(store.LegalName);
        Assert.Equal("11222333000181", store.Cnpj);
        Assert.Equal("Campinas", store.City);
        Assert.Equal("SP", store.State);
        Assert.Equal("aberto 24h", store.Notes);
        Assert.Equal(TestData.FixedNow, store.CreatedAt);
        Assert.Equal(0, store.ReceiptCount);
        Assert.Equal(0m, store.TotalSpent);
        Assert.Null(store.LastPurchaseAt);
    }

    [Fact]
    public async Task Cnpj_must_be_unique_even_with_different_masks()
    {
        await CreateService().CreateAsync(Request("Loja A", "11222333000181"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().CreateAsync(Request("Loja B", "11.222.333/0001-81"), CancellationToken.None));
    }

    [Fact]
    public async Task Stores_without_cnpj_are_allowed()
    {
        await CreateService().CreateAsync(Request("Feira 1"), CancellationToken.None);
        await CreateService().CreateAsync(Request("Feira 2"), CancellationToken.None);

        var result = await CreateService().ListAsync(new StoreQuery(), CancellationToken.None);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Update_keeps_own_cnpj_and_rejects_another_stores_cnpj()
    {
        var first = await CreateService().CreateAsync(Request("Loja A", "11222333000181"), CancellationToken.None);
        var second = await CreateService().CreateAsync(Request("Loja B", "12ABC34501DE35"), CancellationToken.None);

        var updated = await CreateService().UpdateAsync(first.Id, Request("Loja A Renomeada", "11222333000181"), CancellationToken.None);
        Assert.Equal("Loja A Renomeada", updated.TradeName);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().UpdateAsync(second.Id, Request("Loja B", "11222333000181"), CancellationToken.None));
    }

    [Fact]
    public async Task List_searches_filters_sorts_and_paginates()
    {
        var service = CreateService();
        await service.CreateAsync(Request("Atacadão", "11222333000181", "Campinas", "SP"), CancellationToken.None);
        await CreateService().CreateAsync(Request("Carrefour", null, "Rio de Janeiro", "RJ"), CancellationToken.None);
        await CreateService().CreateAsync(Request("Bom Preço", null, "Campinas", "SP"), CancellationToken.None);

        var bySearch = await CreateService().ListAsync(new StoreQuery { Search = "campinas" }, CancellationToken.None);
        Assert.Equal(["Atacadão", "Bom Preço"], bySearch.Items.Select(s => s.TradeName));

        var byCnpj = await CreateService().ListAsync(new StoreQuery { Search = "11.222.333" }, CancellationToken.None);
        Assert.Equal("Atacadão", Assert.Single(byCnpj.Items).TradeName);

        var byState = await CreateService().ListAsync(new StoreQuery { State = "rj" }, CancellationToken.None);
        Assert.Equal("Carrefour", Assert.Single(byState.Items).TradeName);

        var page = await CreateService().ListAsync(
            new StoreQuery { SortBy = "tradeName", SortDirection = "desc", Page = 2, PageSize = 2 }, CancellationToken.None);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("Atacadão", Assert.Single(page.Items).TradeName);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await CreateService().CreateAsync(Request("Mercado 100%"), CancellationToken.None);
        await CreateService().CreateAsync(Request("Mercado 1000"), CancellationToken.None);

        var result = await CreateService().ListAsync(new StoreQuery { Search = "100%" }, CancellationToken.None);

        Assert.Equal("Mercado 100%", Assert.Single(result.Items).TradeName);
    }

    [Fact]
    public async Task Unknown_sort_field_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().ListAsync(new StoreQuery { SortBy = "password" }, CancellationToken.None));
        Assert.True(exception.Errors.ContainsKey("sortBy"));
    }

    [Fact]
    public async Task Indicators_consider_only_confirmed_receipts_and_are_exact()
    {
        int storeId;
        await using (var context = _database.CreateContext())
        {
            var store = TestData.Store();
            var confirmed1 = TestData.Receipt(store, items: [TestData.Item(1, "A", 1m, 10.1000m, 10.10m)]);
            var confirmed2 = TestData.Receipt(store, items: [TestData.Item(1, "B", 1m, 20.2000m, 20.20m)]);
            confirmed2.IssuedAt = new DateTime(2026, 11, 18, 9, 0, 0);
            var draft = TestData.Receipt(store, items: [TestData.Item(1, "C", 1m, 99.9900m, 99.99m)]);
            draft.Status = ReceiptStatus.Draft;
            draft.IssuedAt = new DateTime(2026, 11, 22);
            context.Receipts.AddRange(confirmed1, confirmed2, draft);
            await context.SaveChangesAsync();
            storeId = store.Id;
        }

        var result = await CreateService().GetAsync(storeId, CancellationToken.None);

        Assert.Equal(2, result.ReceiptCount);
        Assert.Equal(30.30m, result.TotalSpent);
        Assert.Equal(new DateTime(2026, 11, 18, 9, 0, 0), result.LastPurchaseAt);

        var sorted = await CreateService().ListAsync(new StoreQuery { SortBy = "totalSpent", SortDirection = "desc" }, CancellationToken.None);
        Assert.Equal(30.30m, sorted.Items[0].TotalSpent);
    }

    [Fact]
    public async Task Delete_blocks_store_with_receipts()
    {
        int storeWithReceipts;
        await using (var context = _database.CreateContext())
        {
            var receipt = TestData.Receipt(TestData.Store());
            context.Receipts.Add(receipt);
            await context.SaveChangesAsync();
            storeWithReceipts = receipt.StoreId;
        }

        var empty = await CreateService().CreateAsync(Request("Sem compras"), CancellationToken.None);

        await CreateService().DeleteAsync(empty.Id, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().DeleteAsync(storeWithReceipts, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetAsync(empty.Id, CancellationToken.None));
    }

    [Fact]
    public async Task FindByCnpj_accepts_masked_value()
    {
        await CreateService().CreateAsync(Request("Loja", "12ABC34501DE35"), CancellationToken.None);

        var found = await CreateService().FindByCnpjAsync("12.abc.345/01de-35", CancellationToken.None);

        Assert.Equal("Loja", found?.TradeName);
        Assert.Null(await CreateService().FindByCnpjAsync("11222333000181", CancellationToken.None));
    }
}
