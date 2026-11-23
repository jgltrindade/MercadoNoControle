using System.Linq.Expressions;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services;

public interface IProductService
{
    Task<PagedResult<ProductListItem>> ListAsync(ProductQuery query, CancellationToken cancellationToken);

    Task<ProductResponse> GetAsync(int id, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(int id, ProductRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);

    Task<ProductAliasResponse> AddAliasAsync(int productId, ProductAliasRequest request, CancellationToken cancellationToken);

    Task<ProductAliasResponse> ConfirmAliasAsync(int productId, int aliasId, CancellationToken cancellationToken);

    Task DeleteAliasAsync(int productId, int aliasId, CancellationToken cancellationToken);
}

public sealed class ProductService(AppDbContext db, TimeProvider timeProvider) : IProductService
{
    private static readonly Dictionary<string, Expression<Func<Product, object?>>> SortFields = BuildSortFields();

    public async Task<PagedResult<ProductListItem>> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();

        if (TextNormalizer.Clean(query.Search) is { } search)
        {
            // Busca pelo nome, marca e código de barras, e também pelas descrições
            // alternativas (normalizadas: sem acento e em maiúsculas).
            var pattern = QueryExtensions.ContainsPattern(search);
            var aliasPattern = QueryExtensions.ContainsPattern(TextNormalizer.NormalizeDescription(search));
            const string escape = QueryExtensions.LikeEscape;
            products = products.Where(p =>
                EF.Functions.Like(p.Name, pattern, escape)
                || EF.Functions.Like(p.Brand, pattern, escape)
                || EF.Functions.Like(p.Barcode, pattern, escape)
                || p.Aliases.Any(a => EF.Functions.Like(a.NormalizedDescription, aliasPattern, escape)));
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.Status is { } status)
        {
            products = products.Where(p => p.Status == status);
        }

        return await products
            .ApplySort(query, SortFields, "name", p => p.Id)
            .Select(p => new ProductListItem(
                p.Id,
                p.Name,
                p.Brand,
                p.CategoryId,
                p.Category != null ? p.Category.Name : null,
                p.Barcode,
                p.Unit,
                p.PackageContent,
                p.Status,
                p.Aliases.Count,
                p.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<ProductResponse> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Aliases)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw NotFound(id);

        return ToResponse(product);
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken cancellationToken)
    {
        var product = new Product { Name = string.Empty, CreatedAt = Now() };
        await ApplyAsync(product, request, cancellationToken);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResponse> UpdateAsync(int id, ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        await ApplyAsync(product, request, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        var inUse = await db.ReceiptItems.AnyAsync(i => i.ProductId == id, cancellationToken)
                    || await db.PriceObservations.AnyAsync(o => o.ProductId == id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException(
                $"O produto \"{product.Name}\" está associado a itens de compras e não pode ser excluído. Considere inativá-lo.");
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductAliasResponse> AddAliasAsync(int productId, ProductAliasRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw NotFound(productId);
        }

        var description = TextNormalizer.CollapseWhitespace(request.Description);
        var normalized = TextNormalizer.NormalizeDescription(description);
        if (await db.ProductAliases.AnyAsync(a => a.ProductId == productId && a.NormalizedDescription == normalized, cancellationToken))
        {
            throw new ConflictException($"A descrição \"{description}\" já está associada a este produto.");
        }

        // Descrição cadastrada manualmente pelo usuário já é uma associação confirmada.
        var alias = new ProductAlias
        {
            ProductId = productId,
            Description = description,
            NormalizedDescription = normalized,
            Confirmed = true,
            Source = AliasSource.Manual,
            CreatedAt = Now()
        };
        db.ProductAliases.Add(alias);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(alias);
    }

    public async Task<ProductAliasResponse> ConfirmAliasAsync(int productId, int aliasId, CancellationToken cancellationToken)
    {
        var alias = await FindAliasAsync(productId, aliasId, cancellationToken);
        alias.Confirmed = true;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(alias);
    }

    public async Task DeleteAliasAsync(int productId, int aliasId, CancellationToken cancellationToken)
    {
        var alias = await FindAliasAsync(productId, aliasId, cancellationToken);
        db.ProductAliases.Remove(alias);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(Product product, ProductRequest request, CancellationToken cancellationToken)
    {
        product.Name = TextNormalizer.CollapseWhitespace(request.Name);
        product.Brand = TextNormalizer.Clean(request.Brand);
        product.CategoryId = request.CategoryId;
        product.Barcode = TextNormalizer.Clean(request.Barcode);
        product.Unit = request.Unit ?? MeasurementUnit.UN;
        product.PackageContent = request.PackageContent;
        product.Status = request.Status;

        if (product.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new BusinessRuleException(
                "Categoria inexistente.",
                new Dictionary<string, string[]> { ["categoryId"] = ["A categoria informada não existe."] });
        }

        if (product.Barcode is { } barcode)
        {
            var owner = await db.Products.AsNoTracking()
                .Where(p => p.Barcode == barcode && p.Id != product.Id)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (owner is not null)
            {
                throw new ConflictException($"O código de barras {barcode} já pertence ao produto \"{owner}\".");
            }
        }
    }

    private async Task<ProductAlias> FindAliasAsync(int productId, int aliasId, CancellationToken cancellationToken) =>
        await db.ProductAliases.SingleOrDefaultAsync(a => a.Id == aliasId && a.ProductId == productId, cancellationToken)
        ?? throw new NotFoundException($"Descrição alternativa {aliasId} não encontrada para o produto {productId}.");

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Brand,
        product.CategoryId,
        product.Category?.Name,
        product.Barcode,
        product.Unit,
        product.PackageContent,
        product.Status,
        product.CreatedAt,
        product.Aliases.OrderBy(a => a.Description).Select(ToResponse).ToList());

    private static ProductAliasResponse ToResponse(ProductAlias alias) =>
        new(alias.Id, alias.Description, alias.Confirmed, alias.Source, alias.CreatedAt);

    private static Dictionary<string, Expression<Func<Product, object?>>> BuildSortFields()
    {
        var fields = QueryExtensions.SortFields<Product>();
        fields["name"] = p => p.Name;
        fields["brand"] = p => p.Brand;
        fields["category"] = p => p.Category != null ? p.Category.Name : null;
        fields["unit"] = p => p.Unit;
        fields["status"] = p => p.Status;
        fields["createdAt"] = p => p.CreatedAt;
        return fields;
    }

    private static NotFoundException NotFound(int id) => new($"Produto {id} não encontrado.");
}
