using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Categories;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> ListAsync(CancellationToken cancellationToken);

    Task<CategoryResponse> GetAsync(int id, CancellationToken cancellationToken);

    Task<CategoryResponse> CreateAsync(CategoryRequest request, CancellationToken cancellationToken);

    Task<CategoryResponse> UpdateAsync(int id, CategoryRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

/// <summary>
/// Categorias são poucas e de uso geral: a listagem é completa (sem paginação), em ordem alfabética.
/// </summary>
public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Description, c.Products.Count))
            .ToListAsync(cancellationToken);

    public async Task<CategoryResponse> GetAsync(int id, CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Description, c.Products.Count))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound(id);

    public async Task<CategoryResponse> CreateAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = new Category { Name = TextNormalizer.CollapseWhitespace(request.Name) };
        Apply(category, request);
        await EnsureUniqueNameAsync(category.Name, null, cancellationToken);

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return new CategoryResponse(category.Id, category.Name, category.Description, 0);
    }

    public async Task<CategoryResponse> UpdateAsync(int id, CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        Apply(category, request);
        await EnsureUniqueNameAsync(category.Name, id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        if (await db.Products.AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            throw new ConflictException($"A categoria \"{category.Name}\" possui produtos e não pode ser excluída.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(Category category, CategoryRequest request)
    {
        category.Name = TextNormalizer.CollapseWhitespace(request.Name);
        category.Description = TextNormalizer.Clean(request.Description);
    }

    private async Task EnsureUniqueNameAsync(string name, int? currentId, CancellationToken cancellationToken)
    {
        // A coluna usa collation NOCASE: a comparação ignora maiúsculas/minúsculas.
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != currentId, cancellationToken))
        {
            throw new ConflictException($"Já existe uma categoria chamada \"{name}\".");
        }
    }

    private static NotFoundException NotFound(int id) => new($"Categoria {id} não encontrada.");
}
