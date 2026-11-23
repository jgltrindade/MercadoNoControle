using MercadoNoControle.Api.Data.Configurations;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Tests.Data;

public class PersistenceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Migrations_cover_the_whole_model()
    {
        using var context = _database.CreateContext();

        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), "Existem alterações no modelo sem migration correspondente.");
    }

    [Fact]
    public async Task Migrations_seed_initial_categories()
    {
        await using var context = _database.CreateContext();

        var names = await context.Categories.OrderBy(c => c.Id).Select(c => c.Name).ToListAsync();

        Assert.Equal(CategoryConfiguration.InitialCategories, names);
    }

    [Fact]
    public async Task Monetary_values_round_trip_exactly_and_are_stored_as_scaled_integers()
    {
        await using (var context = _database.CreateContext())
        {
            var receipt = TestData.Receipt(
                TestData.Store(),
                items:
                [
                    TestData.Item(1, "BANANA PRATA KG", quantity: 1.2350m, unitPrice: 6.9900m, total: 8.63m),
                    TestData.Item(2, "ARROZ TIPO 1 5KG", quantity: 1m, unitPrice: 28.9000m, total: 27.90m, discount: 1.00m)
                ]);
            context.Receipts.Add(receipt);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var receipt = await context.Receipts.Include(r => r.Items).SingleAsync();
            var banana = receipt.Items.Single(i => i.LineNumber == 1);

            Assert.Equal(37.53m, receipt.ProductsTotal);
            Assert.Equal(1.00m, receipt.DiscountTotal);
            Assert.Equal(36.53m, receipt.Total);
            Assert.Equal(1.2350m, banana.Quantity);
            Assert.Equal(6.9900m, banana.UnitPrice);
            Assert.Equal(8.63m, banana.Total);
        }

        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT Total, typeof(Total) FROM Receipts";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(3653L, reader.GetInt64(0));
        Assert.Equal("integer", reader.GetString(1));
    }

    [Fact]
    public async Task Sql_sum_over_scaled_values_is_exact()
    {
        await using (var context = _database.CreateContext())
        {
            var items = Enumerable.Range(1, 1000)
                .Select(line => TestData.Item(line, $"ITEM {line}", 1m, 0.1000m, 0.10m))
                .ToArray();
            context.Receipts.Add(TestData.Receipt(TestData.Store(), items: items));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var sum = await context.ReceiptItems.SumAsync(i => i.Total);
            Assert.Equal(100.00m, sum);
        }
    }

    [Fact]
    public async Task Saving_value_beyond_defined_precision_fails_instead_of_rounding_silently()
    {
        await using var context = _database.CreateContext();
        var receipt = TestData.Receipt(TestData.Store());
        receipt.Total = 10.005m;
        context.Receipts.Add(receipt);

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());

        Assert.NotNull(exception);
        Assert.IsType<ArgumentException>(exception.GetBaseException());
    }

    [Fact]
    public async Task Store_cnpj_is_unique_but_optional()
    {
        await using (var context = _database.CreateContext())
        {
            context.Stores.AddRange(
                TestData.Store("Feira A", cnpj: null),
                TestData.Store("Feira B", cnpj: null),
                TestData.Store("Mercado A", cnpj: "11222333000181"));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            context.Stores.Add(TestData.Store("Mercado Duplicado", cnpj: "11222333000181"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Receipt_access_key_is_unique()
    {
        const string accessKey = "35261111222333000181650010000123451000012345";
        await using (var context = _database.CreateContext())
        {
            var store = TestData.Store();
            context.Receipts.Add(TestData.Receipt(store, accessKey));
            context.Receipts.Add(TestData.Receipt(store, accessKey: null));
            context.Receipts.Add(TestData.Receipt(store, accessKey: null));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var store = await context.Stores.SingleAsync();
            context.Receipts.Add(TestData.Receipt(store, accessKey));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Store_with_receipts_cannot_be_deleted()
    {
        await using (var context = _database.CreateContext())
        {
            context.Receipts.Add(TestData.Receipt(TestData.Store()));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var store = await context.Stores.SingleAsync();
            context.Stores.Remove(store);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Product_and_category_in_use_cannot_be_deleted()
    {
        await using (var context = _database.CreateContext())
        {
            var product = TestData.Product();
            context.Receipts.Add(TestData.Receipt(TestData.Store(), items: [TestData.Item(1, "LEITE INT 1L", 1m, 5.4900m, 5.49m, product: product)]));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            context.Products.Remove(await context.Products.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = _database.CreateContext())
        {
            context.Categories.Remove(await context.Categories.SingleAsync(c => c.Id == 4));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Deleting_receipt_removes_items_and_price_observations_but_keeps_import_record()
    {
        var importId = Guid.NewGuid();
        await using (var context = _database.CreateContext())
        {
            var product = TestData.Product();
            var item = TestData.Item(1, "LEITE INT 1L", 2m, 5.4900m, 10.98m, product: product);
            var receipt = TestData.Receipt(TestData.Store(), items: [item]);
            context.Receipts.Add(receipt);
            context.PriceObservations.Add(new PriceObservation
            {
                Product = product,
                ReceiptItem = item,
                Store = receipt.Store,
                ObservedAt = receipt.IssuedAt,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.Total,
                NormalizedPrice = 5.4900m,
                NormalizedUnit = NormalizedUnit.Liter,
                CreatedAt = TestData.FixedNow
            });
            context.ReceiptImports.Add(new ReceiptImport
            {
                Id = importId,
                Receipt = receipt,
                Status = ReceiptImportStatus.Confirmed,
                ImagePath = "2026/11/nota.jpg",
                ContentType = "image/jpeg",
                CreatedAt = TestData.FixedNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            context.Receipts.Remove(await context.Receipts.SingleAsync());
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            Assert.Equal(0, await context.ReceiptItems.CountAsync());
            Assert.Equal(0, await context.PriceObservations.CountAsync());
            Assert.Equal(1, await context.Products.CountAsync());
            Assert.Equal(1, await context.Stores.CountAsync());

            var import = await context.ReceiptImports.SingleAsync();
            Assert.Equal(importId, import.Id);
            Assert.Null(import.ReceiptId);
        }
    }

    [Fact]
    public async Task Audit_dates_are_read_back_as_utc_while_purchase_dates_stay_local()
    {
        await using (var context = _database.CreateContext())
        {
            context.Receipts.Add(TestData.Receipt(TestData.Store()));
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var receipt = await context.Receipts.Include(r => r.Store).SingleAsync();
            Assert.Equal(DateTimeKind.Utc, receipt.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, receipt.Store.CreatedAt.Kind);
            Assert.Equal(TestData.FixedNow, receipt.CreatedAt);
            Assert.Equal(DateTimeKind.Unspecified, receipt.IssuedAt.Kind);
        }
    }

    [Fact]
    public async Task Receipt_import_exists_without_a_receipt()
    {
        await using (var context = _database.CreateContext())
        {
            context.ReceiptImports.Add(new ReceiptImport
            {
                Id = Guid.NewGuid(),
                Status = ReceiptImportStatus.Pending,
                ImagePath = "2026/11/pendente.jpg",
                ContentType = "image/jpeg",
                FileSize = 1024,
                CreatedAt = TestData.FixedNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var import = await context.ReceiptImports.SingleAsync();
            Assert.Null(import.ReceiptId);
            Assert.Equal(ReceiptImportStatus.Pending, import.Status);
        }
    }
}
