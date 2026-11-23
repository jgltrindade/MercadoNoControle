using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Services.Storage;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Tests.Services;

public class ReceiptServiceTests : IDisposable
{
    private const string AccessKey = "35261111222333000181650010000123451000012345";

    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "mnc-tests", Guid.NewGuid().ToString("N"));
    private readonly int _storeId;
    private readonly int _otherStoreId;
    private readonly int _milkId;
    private readonly int _riceId;

    public ReceiptServiceTests()
    {
        using var context = _database.CreateContext();
        var store = TestData.Store("Mercado A", "11222333000181");
        var other = TestData.Store("Mercado B", "12345678000195");
        var milk = TestData.Product("Leite Integral 1L");
        var rice = TestData.Product("Arroz 5kg", categoryId: 1);
        context.AddRange(store, other, milk, rice);
        context.SaveChanges();
        (_storeId, _otherStoreId, _milkId, _riceId) = (store.Id, other.Id, milk.Id, rice.Id);
    }

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_uploads))
        {
            Directory.Delete(_uploads, recursive: true);
        }
    }

    private ReceiptService CreateService()
    {
        var context = _database.CreateContext();
        var time = new FixedTimeProvider();
        var storage = new ReceiptImageStorage(new StoragePaths("unused.db", _uploads), Options.Create(new ImageUploadOptions()), time);
        return new ReceiptService(context, new PriceObservationRecorder(context), storage, time);
    }

    private ReceiptRequest ValidRequest(int? storeId = null, string? accessKey = null, DateTime? issuedAt = null) => new()
    {
        StoreId = storeId ?? _storeId,
        Number = "12345",
        Series = "1",
        AccessKey = accessKey,
        IssuedAt = issuedAt ?? new DateTime(2026, 11, 15, 14, 30, 0),
        ProductsTotal = 34.39m,
        DiscountTotal = 0.50m,
        AdditionalCharges = 0m,
        Total = 33.89m,
        PaymentMethod = "Cartão de débito",
        Items =
        [
            new ReceiptItemRequest { Description = "LEITE INT 1L", ProductId = _milkId, Quantity = 1, Unit = "un", UnitPrice = 5.49m, Discount = 0.50m, Total = 4.99m },
            new ReceiptItemRequest { Description = "ARROZ T1 5KG", ProductId = _riceId, Quantity = 1, Unit = "UN", UnitPrice = 28.90m, Total = 28.90m }
        ]
    };

    private static ReceiptRequest WithoutProducts(ReceiptRequest request)
    {
        foreach (var item in request.Items)
        {
            item.ProductId = null;
        }

        return request;
    }

    private async Task<ReceiptResponse> CreateConfirmedAsync(ReceiptRequest? request = null)
    {
        var created = await CreateService().CreateAsync(request ?? ValidRequest(), CancellationToken.None);
        return await CreateService().ConfirmAsync(created.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, CancellationToken.None);
    }

    [Fact]
    public async Task Create_saves_a_draft_with_items_preserving_original_descriptions()
    {
        var receipt = await CreateService().CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(ReceiptStatus.Draft, receipt.Status);
        Assert.Equal("Mercado A", receipt.StoreName);
        Assert.Equal(33.89m, receipt.Total);
        Assert.Equal(["LEITE INT 1L", "ARROZ T1 5KG"], receipt.Items.Select(i => i.Description));
        Assert.Equal([1, 2], receipt.Items.Select(i => i.LineNumber));
        Assert.Equal("UN", receipt.Items[0].Unit);
        Assert.Equal("Leite Integral 1L", receipt.Items[0].ProductName);
        Assert.Empty(receipt.Warnings);

        // Rascunho não gera histórico de preços (RN01/RN03).
        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.PriceObservations.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_unknown_store_and_products()
    {
        var unknownStore = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(ValidRequest(storeId: 999), CancellationToken.None));
        Assert.True(unknownStore.Errors.ContainsKey("storeId"));

        var request = ValidRequest();
        request.Items[1].ProductId = 999;
        var unknownProduct = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().CreateAsync(request, CancellationToken.None));
        Assert.True(unknownProduct.Errors.ContainsKey("items[1].productId"));
    }

    [Fact]
    public async Task Access_key_identifies_the_receipt_and_blocks_duplicates()
    {
        var first = await CreateService().CreateAsync(ValidRequest(accessKey: "3526 1111 2223 3300 0181 6500 1000 0123 4510 0001 2345"), CancellationToken.None);
        Assert.Equal(AccessKey, first.AccessKey);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().CreateAsync(ValidRequest(accessKey: AccessKey), CancellationToken.None));
        Assert.Equal(first.Id, conflict.Extensions["existingReceiptId"]);
    }

    [Fact]
    public async Task Confirm_requires_items()
    {
        var request = ValidRequest();
        request.Items.Clear();
        var draft = await CreateService().CreateAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_without_warnings_generates_price_history_and_learns_aliases()
    {
        var request = ValidRequest();
        request.Items.Add(new ReceiptItemRequest { Description = "ITEM SEM PRODUTO", Quantity = 1, UnitPrice = 0m, Total = 0m });
        var draft = await CreateService().CreateAsync(request, CancellationToken.None);

        var confirmed = await CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest(), CancellationToken.None);

        Assert.Equal(ReceiptStatus.Confirmed, confirmed.Status);
        await using var context = _database.CreateContext();
        var observations = await context.PriceObservations.OrderBy(o => o.ProductId).ToListAsync();
        Assert.Equal(2, observations.Count); // o item sem produto não gera observação (RN06)
        var milk = observations.Single(o => o.ProductId == _milkId);
        Assert.Equal(_storeId, milk.StoreId);
        Assert.Equal(new DateTime(2026, 11, 15, 14, 30, 0), milk.ObservedAt);
        Assert.Equal(5.49m, milk.UnitPrice);
        Assert.Equal(4.99m, milk.TotalPrice);
        Assert.Null(milk.NormalizedPrice);

        var aliases = await context.ProductAliases.OrderBy(a => a.ProductId).ToListAsync();
        Assert.Equal(2, aliases.Count);
        Assert.All(aliases, a => Assert.True(a.Confirmed));
        Assert.All(aliases, a => Assert.Equal(AliasSource.Receipt, a.Source));
        Assert.Equal("LEITE INT 1L", aliases.Single(a => a.ProductId == _milkId).NormalizedDescription);
    }

    [Fact]
    public async Task Confirm_with_warnings_requires_acknowledgement()
    {
        var request = ValidRequest();
        request.Total = 40.00m;
        var draft = await CreateService().CreateAsync(request, CancellationToken.None);
        Assert.Contains(draft.Warnings, w => w.Code == ReceiptConsistency.TotalMismatch);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest(), CancellationToken.None));
        var warnings = Assert.IsAssignableFrom<IReadOnlyList<ReceiptWarning>>(conflict.Extensions["warnings"]);
        Assert.Contains(warnings, w => w.Code == ReceiptConsistency.TotalMismatch);
        Assert.Equal(ReceiptStatus.Draft, (await CreateService().GetAsync(draft.Id, CancellationToken.None)).Status);

        var confirmed = await CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task Possible_duplicates_are_reported_but_do_not_block_legitimate_purchases()
    {
        await CreateConfirmedAsync();

        // Mesmo supermercado, mesma data e mesmo total, sem chave de acesso.
        var sameDay = ValidRequest(issuedAt: new DateTime(2026, 11, 15, 19, 0, 0));
        sameDay.Number = null;
        var draft = await CreateService().CreateAsync(sameDay, CancellationToken.None);
        var duplicate = Assert.Single(draft.PossibleDuplicates);
        Assert.Equal("Mesma data e mesmo valor total", duplicate.Reason);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest(), CancellationToken.None));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DuplicateCandidate>>(conflict.Extensions["possibleDuplicates"]));

        var confirmed = await CreateService().ConfirmAsync(draft.Id, new ConfirmReceiptRequest { AcknowledgeWarnings = true }, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task Same_number_and_series_is_a_possible_duplicate_only_in_the_same_store()
    {
        await CreateConfirmedAsync();

        var sameNumber = await CreateService().CreateAsync(ValidRequest(issuedAt: new DateTime(2026, 11, 20)), CancellationToken.None);
        Assert.Equal("Mesmo número e série", Assert.Single(sameNumber.PossibleDuplicates).Reason);

        var otherStore = await CreateService().CreateAsync(ValidRequest(storeId: _otherStoreId), CancellationToken.None);
        Assert.Empty(otherStore.PossibleDuplicates);
    }

    [Fact]
    public async Task Updating_a_confirmed_receipt_keeps_item_ids_and_resyncs_price_history()
    {
        var confirmed = await CreateConfirmedAsync();
        var milkItem = confirmed.Items[0];
        var riceItem = confirmed.Items[1];

        var request = ValidRequest();
        request.Items =
        [
            // Leite: preço corrigido e mantido.
            new ReceiptItemRequest { Id = milkItem.Id, Description = "LEITE INT 1L", ProductId = _milkId, Quantity = 2, UnitPrice = 5.49m, Total = 10.98m },
            // Arroz removido; novo item sem produto.
            new ReceiptItemRequest { Description = "PAO FRANCES", Quantity = 0.5m, Unit = "KG", UnitPrice = 15.00m, Total = 7.50m }
        ];
        request.ProductsTotal = 18.48m;
        request.DiscountTotal = 0m;
        request.Total = 18.48m;

        var updated = await CreateService().UpdateAsync(confirmed.Id, request, CancellationToken.None);

        Assert.Equal(ReceiptStatus.Confirmed, updated.Status);
        Assert.Equal(milkItem.Id, updated.Items[0].Id);
        Assert.DoesNotContain(updated.Items, i => i.Id == riceItem.Id);
        Assert.NotNull(updated.UpdatedAt);

        await using var context = _database.CreateContext();
        var observation = Assert.Single(await context.PriceObservations.ToListAsync());
        Assert.Equal(milkItem.Id, observation.ReceiptItemId);
        Assert.Equal(2m, observation.Quantity);
        Assert.Equal(10.98m, observation.TotalPrice);
    }

    [Fact]
    public async Task Associating_a_product_later_creates_its_observation()
    {
        var confirmed = await CreateConfirmedAsync(WithoutProducts(ValidRequest()));
        await using (var context = _database.CreateContext())
        {
            Assert.Equal(0, await context.PriceObservations.CountAsync());
        }

        var request = ValidRequest();
        request.Items[0].Id = confirmed.Items[0].Id;
        request.Items[1].Id = confirmed.Items[1].Id;
        request.Items[1].ProductId = null;
        await CreateService().UpdateAsync(confirmed.Id, request, CancellationToken.None);

        await using (var context = _database.CreateContext())
        {
            var observation = Assert.Single(await context.PriceObservations.ToListAsync());
            Assert.Equal(_milkId, observation.ProductId);
        }
    }

    [Fact]
    public async Task Editing_a_draft_does_not_create_price_history()
    {
        var draft = await CreateService().CreateAsync(ValidRequest(), CancellationToken.None);

        await CreateService().UpdateAsync(draft.Id, ValidRequest(), CancellationToken.None);

        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.PriceObservations.CountAsync());
        Assert.Equal(2, await context.ReceiptItems.CountAsync());
    }

    [Fact]
    public async Task Update_rejects_item_ids_from_another_receipt_and_empty_confirmed_receipts()
    {
        var first = await CreateConfirmedAsync();
        var other = await CreateService().CreateAsync(ValidRequest(storeId: _otherStoreId), CancellationToken.None);

        var foreignItem = ValidRequest();
        foreignItem.Items[0].Id = first.Items[0].Id;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().UpdateAsync(other.Id, foreignItem, CancellationToken.None));
        Assert.True(error.Errors.ContainsKey("items[0].id"));

        var empty = ValidRequest();
        empty.Items.Clear();
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().UpdateAsync(first.Id, empty, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_items_and_price_history_from_reports()
    {
        var confirmed = await CreateConfirmedAsync();

        await CreateService().DeleteAsync(confirmed.Id, CancellationToken.None);

        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.Receipts.CountAsync());
        Assert.Equal(0, await context.ReceiptItems.CountAsync());
        Assert.Equal(0, await context.PriceObservations.CountAsync());
        Assert.Equal(2, await context.ProductAliases.CountAsync()); // descrições aprendidas permanecem
        var store = await new StoreService(context, new FixedTimeProvider()).GetAsync(_storeId, CancellationToken.None);
        Assert.Equal(0, store.ReceiptCount);
        Assert.Equal(0m, store.TotalSpent);
    }

    [Fact]
    public async Task Store_indicators_reflect_confirmed_receipts_only()
    {
        await CreateConfirmedAsync();
        await CreateService().CreateAsync(ValidRequest(issuedAt: new DateTime(2026, 11, 21)), CancellationToken.None);

        await using var context = _database.CreateContext();
        var store = await new StoreService(context, new FixedTimeProvider()).GetAsync(_storeId, CancellationToken.None);
        Assert.Equal(1, store.ReceiptCount);
        Assert.Equal(33.89m, store.TotalSpent);
    }

    [Fact]
    public async Task List_filters_by_store_status_period_and_search_sorted_by_date_desc()
    {
        await CreateConfirmedAsync(ValidRequest(issuedAt: new DateTime(2026, 11, 1, 10, 0, 0)));
        var request = ValidRequest(issuedAt: new DateTime(2026, 11, 30, 23, 59, 0));
        request.Number = "999";
        await CreateService().CreateAsync(request, CancellationToken.None);
        await CreateConfirmedAsync(ValidRequest(storeId: _otherStoreId, issuedAt: new DateTime(2026, 10, 15)));

        var all = await CreateService().ListAsync(new ReceiptQuery(), CancellationToken.None);
        Assert.Equal(
            [new DateTime(2026, 11, 30, 23, 59, 0), new DateTime(2026, 11, 1, 10, 0, 0), new DateTime(2026, 10, 15)],
            all.Items.Select(r => r.IssuedAt));
        Assert.Equal(2, all.Items[0].ItemCount);

        var november = await CreateService().ListAsync(
            new ReceiptQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 30) }, CancellationToken.None);
        Assert.Equal(2, november.TotalCount);

        var drafts = await CreateService().ListAsync(new ReceiptQuery { Status = ReceiptStatus.Draft }, CancellationToken.None);
        Assert.Equal("999", Assert.Single(drafts.Items).Number);

        var byStore = await CreateService().ListAsync(new ReceiptQuery { StoreId = _otherStoreId }, CancellationToken.None);
        Assert.Equal("Mercado B", Assert.Single(byStore.Items).StoreName);

        var bySearch = await CreateService().ListAsync(new ReceiptQuery { Search = "mercado b" }, CancellationToken.None);
        Assert.Single(bySearch.Items);

        var byTotal = await CreateService().ListAsync(new ReceiptQuery { SortBy = "total", SortDirection = "asc" }, CancellationToken.None);
        Assert.Equal(3, byTotal.TotalCount);

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().ListAsync(
            new ReceiptQuery { From = new DateOnly(2026, 12, 1), To = new DateOnly(2026, 11, 1) }, CancellationToken.None));
    }

    [Fact]
    public async Task Images_are_attached_served_and_detached_without_deleting_the_file()
    {
        var draft = await CreateService().CreateAsync(ValidRequest(), CancellationToken.None);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];

        var withImage = await CreateService().AttachImageAsync(draft.Id, new MemoryStream(png), png.Length, CancellationToken.None);
        Assert.True(withImage.HasImage);

        var (content, contentType) = await CreateService().OpenImageAsync(draft.Id, CancellationToken.None);
        await using (content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            Assert.Equal(png, copy.ToArray());
        }
        Assert.Equal("image/png", contentType);

        var detached = await CreateService().RemoveImageAsync(draft.Id, CancellationToken.None);
        Assert.False(detached.HasImage);
        Assert.Single(Directory.GetFiles(_uploads, "*.png", SearchOption.AllDirectories));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().OpenImageAsync(draft.Id, CancellationToken.None));
    }
}
