using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Imports;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Services.Storage;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Tests.Services;

public class ReceiptDraftBuilderTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public void Dispose() => _database.Dispose();

    private ReceiptDraftBuilder CreateBuilder() => new(_database.CreateContext());

    private async Task<(int StoreId, int RiceId)> SeedAsync()
    {
        await using var context = _database.CreateContext();
        var store = TestData.Store("Mercado Teste", "11222333000181");
        var rice = TestData.Product("Arroz 5kg", categoryId: 1);
        context.AddRange(store, rice);
        context.ProductAliases.Add(new ProductAlias
        {
            Product = rice, Description = "ARROZ T1 5KG", NormalizedDescription = "ARROZ T1 5KG", Confirmed = true, CreatedAt = TestData.FixedNow
        });
        await context.SaveChangesAsync();
        return (store.Id, rice.Id);
    }

    [Fact]
    public async Task Builds_a_validated_draft_matching_store_and_products()
    {
        var (storeId, riceId) = await SeedAsync();

        var draft = await CreateBuilder().BuildAsync(FakeReceiptReader.SampleExtraction(), CancellationToken.None);

        Assert.Equal(storeId, draft.StoreId);
        Assert.Equal("Mercado Teste", draft.StoreName);
        Assert.Equal("11222333000181", draft.ExtractedCnpj);
        Assert.Equal("35261111222333000181650010000123451000012345", draft.AccessKey);
        Assert.Equal(new DateTime(2026, 11, 15, 14, 30, 0), draft.IssuedAt);
        Assert.Equal(36.53m, draft.Total);
        Assert.Empty(draft.Warnings);

        var banana = draft.Items[0];
        Assert.Equal(8.63m, banana.Total);
        Assert.Null(banana.ProductId);
        Assert.Equal(0, banana.MatchCount);

        var rice = draft.Items[1];
        Assert.Equal(27.90m, rice.Total); // líquido: 28,90 − 1,00
        Assert.Equal(riceId, rice.ProductId);
        Assert.Equal("Arroz 5kg", rice.ProductName);
    }

    [Fact]
    public async Task Unknown_store_keeps_the_values_read_for_registration()
    {
        var draft = await CreateBuilder().BuildAsync(FakeReceiptReader.SampleExtraction("12.345.678/0001-95"), CancellationToken.None);

        Assert.Null(draft.StoreId);
        Assert.Equal("MERCADO TESTE LTDA", draft.ExtractedStoreName);
        Assert.Equal("12345678000195", draft.ExtractedCnpj);
        Assert.Contains(draft.Warnings, w => w.Code == ReceiptDraftBuilder.StoreNotFound);
    }

    [Fact]
    public async Task Invalid_values_are_not_filled_and_are_reported_with_what_was_read()
    {
        var sample = FakeReceiptReader.SampleExtraction("11.222.333/0001-99");
        var extraction = sample with
        {
            Receipt = sample.Receipt! with { AccessKey = "1234 5678", IssueDate = "15/11/2026" }
        };

        var draft = await CreateBuilder().BuildAsync(extraction, CancellationToken.None);

        Assert.Null(draft.StoreId);
        Assert.Null(draft.AccessKey);
        Assert.Null(draft.IssuedAt);
        Assert.Contains(draft.Warnings, w => w.Code == ReceiptDraftBuilder.InvalidCnpj && w.Message.Contains("11.222.333/0001-99"));
        Assert.Contains(draft.Warnings, w => w.Code == ReceiptDraftBuilder.InvalidAccessKey && w.Message.Contains("1234 5678"));
        Assert.Contains(draft.Warnings, w => w.Code == ReceiptDraftBuilder.InvalidDate);
    }

    [Fact]
    public async Task Missing_values_stay_null_and_inconsistent_totals_are_flagged()
    {
        await SeedAsync();
        var sample = FakeReceiptReader.SampleExtraction();

        var missing = await CreateBuilder().BuildAsync(
            sample with { Items = [new ExtractedItem("ITEM ILEGIVEL", null, null, null, null, null)] }, CancellationToken.None);
        Assert.Null(missing.Items[0].Quantity);
        Assert.Null(missing.Items[0].Total);
        Assert.Contains(missing.Warnings, w => w.Code == ReceiptDraftBuilder.MissingValues);

        var inconsistent = await CreateBuilder().BuildAsync(
            sample with { Receipt = sample.Receipt! with { Total = 40.00m } }, CancellationToken.None);
        Assert.Contains(inconsistent.Warnings, w => w.Code == ReceiptConsistency.TotalMismatch);
    }

    [Fact]
    public async Task Ambiguous_descriptions_are_not_associated_automatically()
    {
        await using (var context = _database.CreateContext())
        {
            var a = TestData.Product("Arroz Marca A");
            var b = TestData.Product("Arroz Marca B");
            context.AddRange(a, b);
            context.ProductAliases.AddRange(
                new ProductAlias { Product = a, Description = "ARROZ T1 5KG", NormalizedDescription = "ARROZ T1 5KG", Confirmed = true, CreatedAt = TestData.FixedNow },
                new ProductAlias { Product = b, Description = "ARROZ T1 5KG", NormalizedDescription = "ARROZ T1 5KG", Confirmed = true, CreatedAt = TestData.FixedNow });
            await context.SaveChangesAsync();
        }

        var draft = await CreateBuilder().BuildAsync(FakeReceiptReader.SampleExtraction(), CancellationToken.None);

        Assert.Null(draft.Items[1].ProductId);
        Assert.Equal(2, draft.Items[1].MatchCount);
    }

    [Fact]
    public async Task Invalid_or_unreadable_json_produces_no_draft()
    {
        Assert.Null(await CreateBuilder().BuildAsync("{inválido", CancellationToken.None));
        Assert.Null(await CreateBuilder().BuildAsync("""{"isReceipt":false,"unreadableReason":"foto borrada"}""", CancellationToken.None));
        Assert.Null(await CreateBuilder().BuildAsync((string?)null, CancellationToken.None));
    }
}

public class ReceiptImportServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "mnc-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeReceiptReader _reader = new();
    private readonly ReceiptImportQueue _queue = new();
    private readonly int _storeId;

    public ReceiptImportServiceTests()
    {
        using var context = _database.CreateContext();
        var store = TestData.Store("Mercado Teste", "11222333000181");
        context.Stores.Add(store);
        context.SaveChanges();
        _storeId = store.Id;
    }

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_uploads))
        {
            Directory.Delete(_uploads, recursive: true);
        }
    }

    private ReceiptImageStorage Storage() =>
        new(new StoragePaths("unused.db", _uploads), Options.Create(new ImageUploadOptions()), new FixedTimeProvider());

    private ReceiptImportService CreateService()
    {
        var context = _database.CreateContext();
        var time = new FixedTimeProvider();
        var receipts = new ReceiptService(context, new PriceObservationRecorder(context), Storage(), time);
        return new ReceiptImportService(context, _reader, Storage(), _queue, new ReceiptDraftBuilder(context), receipts, time);
    }

    private ReceiptImportProcessor CreateProcessor(IReceiptReader? reader = null) =>
        new(_database.CreateContext(), reader ?? _reader, Storage(), new FixedTimeProvider(), NullLogger<ReceiptImportProcessor>.Instance);

    private async Task<Guid> UploadAndProcessAsync()
    {
        var import = await CreateService().UploadAsync(new MemoryStream(FakeReceiptReader.JpegBytes), FakeReceiptReader.JpegBytes.Length, "nota.jpg", CancellationToken.None);
        await CreateProcessor().ProcessAsync(import.Id, CancellationToken.None);
        return import.Id;
    }

    private ConfirmImportRequest ReviewedRequest(bool acknowledge = false) => new()
    {
        StoreId = _storeId,
        Number = "123456",
        Series = "1",
        AccessKey = "35261111222333000181650010000123451000012345",
        IssuedAt = new DateTime(2026, 11, 15, 14, 30, 0),
        ProductsTotal = 37.53m,
        DiscountTotal = 1.00m,
        AdditionalCharges = 0m,
        Total = 36.53m,
        AcknowledgeWarnings = acknowledge,
        Items =
        [
            new ReceiptItemRequest { Description = "BANANA PRATA KG", Quantity = 1.235m, Unit = "KG", UnitPrice = 6.99m, Total = 8.63m },
            new ReceiptItemRequest { Description = "ARROZ T1 5KG", Quantity = 1m, Unit = "UN", UnitPrice = 28.90m, Discount = 1.00m, Total = 27.90m }
        ]
    };

    [Fact]
    public async Task Upload_stores_the_image_creates_a_pending_import_and_queues_it()
    {
        var import = await CreateService().UploadAsync(new MemoryStream(FakeReceiptReader.JpegBytes), FakeReceiptReader.JpegBytes.Length, @"C:\fotos\nota.jpg", CancellationToken.None);

        Assert.Equal(ReceiptImportStatus.Pending, import.Status);
        Assert.Equal("nota.jpg", import.OriginalFileName);
        Assert.Null(import.ReceiptId);
        Assert.Single(Directory.GetFiles(_uploads, "*.jpg", SearchOption.AllDirectories));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var queued = _queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await queued.MoveNextAsync());
        Assert.Equal(import.Id, queued.Current);
    }

    [Fact]
    public async Task Successful_reading_awaits_review_with_a_draft_and_records_the_provider()
    {
        var id = await UploadAndProcessAsync();

        var import = await CreateService().GetAsync(id, CancellationToken.None);

        Assert.Equal(ReceiptImportStatus.AwaitingReview, import.Status);
        Assert.Equal("Fake", import.Provider);
        Assert.Equal("fake-model", import.Model);
        Assert.Equal(1, import.AttemptCount);
        Assert.Equal(FakeReceiptReader.JpegBytes.Length, _reader.LastImageLength);
        Assert.NotNull(import.Draft);
        Assert.Equal(_storeId, import.Draft.StoreId);
        Assert.Equal(2, import.Draft.Items.Count);

        // Nada foi registrado antes da confirmação (RN01).
        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.Receipts.CountAsync());
    }

    [Theory]
    [InlineData(ReceiptReaderErrorKind.Timeout, "A leitura demorou")]
    [InlineData(ReceiptReaderErrorKind.Unavailable, "indisponível")]
    [InlineData(ReceiptReaderErrorKind.InvalidResponse, "formato inválido")]
    [InlineData(ReceiptReaderErrorKind.NotConfigured, "não está configurada")]
    public async Task Reader_failures_mark_the_import_as_failed_with_the_message(ReceiptReaderErrorKind kind, string message)
    {
        _reader.Exception = new ReceiptReaderException(kind, $"{message}.");

        var id = await UploadAndProcessAsync();
        var import = await CreateService().GetAsync(id, CancellationToken.None);

        Assert.Equal(ReceiptImportStatus.Failed, import.Status);
        Assert.Equal($"{message}.", import.Error);
        Assert.Null(import.Draft);
    }

    [Fact]
    public async Task Unexpected_errors_and_non_receipt_images_also_fail_gracefully()
    {
        _reader.Exception = new InvalidOperationException("boom");
        var crashed = await CreateService().GetAsync(await UploadAndProcessAsync(), CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.Failed, crashed.Status);
        Assert.DoesNotContain("boom", crashed.Error);

        _reader.Exception = null;
        _reader.Extraction = new ReceiptExtraction { IsReceipt = false, UnreadableReason = "a foto está desfocada" };
        var unreadable = await CreateService().GetAsync(await UploadAndProcessAsync(), CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.Failed, unreadable.Status);
        Assert.Contains("a foto está desfocada", unreadable.Error);
    }

    [Fact]
    public async Task Processing_runs_only_for_pending_imports()
    {
        var id = await UploadAndProcessAsync();

        await CreateProcessor().ProcessAsync(id, CancellationToken.None);
        await CreateProcessor().ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, _reader.Calls);
    }

    [Fact]
    public async Task Failed_imports_can_be_reprocessed()
    {
        _reader.Exception = new ReceiptReaderException(ReceiptReaderErrorKind.Unavailable, "fora do ar");
        var id = await UploadAndProcessAsync();

        _reader.Exception = null;
        var requeued = await CreateService().ReprocessAsync(id, CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.Pending, requeued.Status);
        Assert.Null(requeued.Error);

        await CreateProcessor().ProcessAsync(id, CancellationToken.None);
        var import = await CreateService().GetAsync(id, CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.AwaitingReview, import.Status);
        Assert.Equal(2, import.AttemptCount);
    }

    [Fact]
    public async Task Confirmation_creates_the_purchase_atomically_with_image_history_and_link()
    {
        var id = await UploadAndProcessAsync();

        var receipt = await CreateService().ConfirmAsync(id, ReviewedRequest(), CancellationToken.None);

        Assert.Equal(ReceiptStatus.Confirmed, receipt.Status);
        Assert.True(receipt.HasImage);
        Assert.Equal(36.53m, receipt.Total);

        var import = await CreateService().GetAsync(id, CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.Confirmed, import.Status);
        Assert.Equal(receipt.Id, import.ReceiptId);

        var processing = await CreateService().ListForReceiptAsync(receipt.Id, CancellationToken.None);
        Assert.Equal(id, Assert.Single(processing).Id);

        await Assert.ThrowsAsync<ConflictException>(() => CreateService().ConfirmAsync(id, ReviewedRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Confirmation_with_warnings_saves_nothing_until_acknowledged()
    {
        var id = await UploadAndProcessAsync();
        var request = ReviewedRequest();
        request.Total = 40.00m;

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => CreateService().ConfirmAsync(id, request, CancellationToken.None));
        Assert.True(conflict.Extensions.ContainsKey("warnings"));

        await using (var context = _database.CreateContext())
        {
            Assert.Equal(0, await context.Receipts.CountAsync());
            Assert.Equal(ReceiptImportStatus.AwaitingReview, (await context.ReceiptImports.SingleAsync()).Status);
        }

        request.AcknowledgeWarnings = true;
        var receipt = await CreateService().ConfirmAsync(id, request, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Confirmed, receipt.Status);
    }

    [Fact]
    public async Task Failed_readings_can_still_be_completed_manually_with_the_image()
    {
        _reader.Exception = new ReceiptReaderException(ReceiptReaderErrorKind.NotConfigured, "sem chave");
        var id = await UploadAndProcessAsync();

        var receipt = await CreateService().ConfirmAsync(id, ReviewedRequest(), CancellationToken.None);

        Assert.True(receipt.HasImage);
        await using var context = _database.CreateContext();
        Assert.Equal(ReceiptImportStatus.Confirmed, (await context.ReceiptImports.SingleAsync()).Status);
        Assert.Equal(0, await context.PriceObservations.CountAsync()); // itens sem produto associado: sem histórico
    }

    [Fact]
    public async Task Pending_imports_cannot_be_confirmed_and_discarded_imports_close()
    {
        var pending = await CreateService().UploadAsync(new MemoryStream(FakeReceiptReader.JpegBytes), FakeReceiptReader.JpegBytes.Length, "nota.jpg", CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().ConfirmAsync(pending.Id, ReviewedRequest(), CancellationToken.None));

        var discarded = await CreateService().DiscardAsync(pending.Id, CancellationToken.None);
        Assert.Equal(ReceiptImportStatus.Discarded, discarded.Status);
        Assert.Empty(await CreateService().ListOpenAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().ReprocessAsync(pending.Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void Reader_status_explains_why_reading_is_unavailable()
    {
        var context = _database.CreateContext();
        var service = new ReceiptImportService(
            context, new UnavailableReceiptReader("Chave ausente."), Storage(), _queue, new ReceiptDraftBuilder(context),
            new ReceiptService(context, new PriceObservationRecorder(context), Storage(), new FixedTimeProvider()), new FixedTimeProvider());

        var status = service.GetReaderStatus();

        Assert.False(status.Available);
        Assert.Equal("Chave ausente.", status.Message);
    }
}
