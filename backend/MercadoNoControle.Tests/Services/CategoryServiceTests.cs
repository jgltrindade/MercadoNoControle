using MercadoNoControle.Api.DTOs.Categories;
using MercadoNoControle.Api.Services;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Services;

public class CategoryServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public void Dispose() => _database.Dispose();

    private CategoryService CreateService() => new(_database.CreateContext());

    [Fact]
    public async Task List_returns_seeded_categories_in_alphabetical_order_with_product_count()
    {
        await using (var context = _database.CreateContext())
        {
            context.Products.Add(TestData.Product(categoryId: 4));
            await context.SaveChangesAsync();
        }

        var categories = await CreateService().ListAsync(CancellationToken.None);

        Assert.Equal(10, categories.Count);
        Assert.Equal("Bebidas", categories[0].Name);
        Assert.Equal(1, categories.Single(c => c.Name == "Laticínios").ProductCount);
        Assert.Equal(0, categories.Single(c => c.Name == "Bebidas").ProductCount);
    }

    [Fact]
    public async Task Create_trims_values_and_rejects_duplicate_names_ignoring_case()
    {
        var service = CreateService();

        var created = await service.CreateAsync(new CategoryRequest { Name = "  Pet   shop ", Description = "  " }, CancellationToken.None);

        Assert.Equal("Pet shop", created.Name);
        Assert.Null(created.Description);
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().CreateAsync(new CategoryRequest { Name = "PET SHOP" }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_changes_name_and_allows_keeping_its_own_name()
    {
        var updated = await CreateService().UpdateAsync(1, new CategoryRequest { Name = "mercearia", Description = "Secos" }, CancellationToken.None);

        Assert.Equal("mercearia", updated.Name);
        Assert.Equal("Secos", updated.Description);
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().UpdateAsync(1, new CategoryRequest { Name = "Bebidas" }, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_unused_category_and_blocks_category_in_use()
    {
        await using (var context = _database.CreateContext())
        {
            context.Products.Add(TestData.Product(categoryId: 4));
            await context.SaveChangesAsync();
        }

        await CreateService().DeleteAsync(10, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => CreateService().DeleteAsync(4, CancellationToken.None));

        var remaining = await CreateService().ListAsync(CancellationToken.None);
        Assert.DoesNotContain(remaining, c => c.Id == 10);
        Assert.Contains(remaining, c => c.Id == 4);
    }

    [Fact]
    public async Task Missing_category_raises_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetAsync(999, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().UpdateAsync(999, new CategoryRequest { Name = "X" }, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().DeleteAsync(999, CancellationToken.None));
    }
}
