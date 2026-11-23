using System.Linq.Expressions;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Validators;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services;

public interface IStoreService
{
    Task<PagedResult<StoreResponse>> ListAsync(StoreQuery query, CancellationToken cancellationToken);

    Task<StoreResponse> GetAsync(int id, CancellationToken cancellationToken);

    Task<StoreResponse?> FindByCnpjAsync(string cnpj, CancellationToken cancellationToken);

    Task<StoreResponse> CreateAsync(StoreRequest request, CancellationToken cancellationToken);

    Task<StoreResponse> UpdateAsync(int id, StoreRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

public sealed class StoreService(AppDbContext db, TimeProvider timeProvider) : IStoreService
{
    private static readonly Dictionary<string, Expression<Func<Store, object?>>> SortFields = BuildSortFields();

    public async Task<PagedResult<StoreResponse>> ListAsync(StoreQuery query, CancellationToken cancellationToken)
    {
        var stores = db.Stores.AsNoTracking();

        if (TextNormalizer.Clean(query.Search) is { } search)
        {
            var pattern = QueryExtensions.ContainsPattern(search);
            var cnpjPattern = QueryExtensions.ContainsPattern(CnpjValidator.Normalize(search));
            const string escape = QueryExtensions.LikeEscape;
            stores = stores.Where(s =>
                EF.Functions.Like(s.TradeName, pattern, escape)
                || EF.Functions.Like(s.LegalName, pattern, escape)
                || EF.Functions.Like(s.City, pattern, escape)
                || EF.Functions.Like(s.Cnpj, cnpjPattern, escape));
        }

        if (TextNormalizer.Clean(query.State) is { } state)
        {
            var uf = state.ToUpperInvariant();
            stores = stores.Where(s => s.State == uf);
        }

        return await stores
            .ApplySort(query, SortFields, "tradeName", s => s.Id)
            .Select(ToResponse)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<StoreResponse> GetAsync(int id, CancellationToken cancellationToken) =>
        await db.Stores.AsNoTracking().Where(s => s.Id == id).Select(ToResponse).SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound(id);

    public async Task<StoreResponse?> FindByCnpjAsync(string cnpj, CancellationToken cancellationToken)
    {
        var normalized = CnpjValidator.Normalize(cnpj);
        return await db.Stores.AsNoTracking().Where(s => s.Cnpj == normalized).Select(ToResponse).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StoreResponse> CreateAsync(StoreRequest request, CancellationToken cancellationToken)
    {
        var store = new Store { TradeName = string.Empty, CreatedAt = timeProvider.GetUtcNow().UtcDateTime };
        Apply(store, request);
        await EnsureUniqueCnpjAsync(store.Cnpj, null, cancellationToken);

        db.Stores.Add(store);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(store.Id, cancellationToken);
    }

    public async Task<StoreResponse> UpdateAsync(int id, StoreRequest request, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        Apply(store, request);
        await EnsureUniqueCnpjAsync(store.Cnpj, id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FindAsync([id], cancellationToken) ?? throw NotFound(id);
        if (await db.Receipts.AnyAsync(r => r.StoreId == id, cancellationToken))
        {
            throw new ConflictException($"O supermercado \"{store.TradeName}\" possui compras registradas e não pode ser excluído.");
        }

        db.Stores.Remove(store);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(Store store, StoreRequest request)
    {
        store.TradeName = TextNormalizer.CollapseWhitespace(request.TradeName);
        store.LegalName = TextNormalizer.Clean(request.LegalName);
        store.Cnpj = TextNormalizer.Clean(request.Cnpj) is { } cnpj ? CnpjValidator.Normalize(cnpj) : null;
        store.Address = TextNormalizer.Clean(request.Address);
        store.City = TextNormalizer.Clean(request.City);
        store.State = TextNormalizer.Clean(request.State)?.ToUpperInvariant();
        store.Notes = request.Notes?.Trim() is { Length: > 0 } notes ? notes : null;
    }

    private async Task EnsureUniqueCnpjAsync(string? cnpj, int? currentId, CancellationToken cancellationToken)
    {
        if (cnpj is null)
        {
            return;
        }

        var existing = await db.Stores.AsNoTracking()
            .Where(s => s.Cnpj == cnpj && s.Id != currentId)
            .Select(s => s.TradeName)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException($"O CNPJ informado já pertence ao supermercado \"{existing}\".");
        }
    }

    /// <summary>Indicadores consideram somente compras confirmadas (RN09).</summary>
    private static readonly Expression<Func<Store, StoreResponse>> ToResponse = s => new StoreResponse(
        s.Id,
        s.TradeName,
        s.LegalName,
        s.Cnpj,
        s.Address,
        s.City,
        s.State,
        s.Notes,
        s.CreatedAt,
        s.Receipts.Count(r => r.Status == ReceiptStatus.Confirmed),
        s.Receipts.Where(r => r.Status == ReceiptStatus.Confirmed).Sum(r => r.Total),
        s.Receipts.Where(r => r.Status == ReceiptStatus.Confirmed).Max(r => (DateTime?)r.IssuedAt));

    private static Dictionary<string, Expression<Func<Store, object?>>> BuildSortFields()
    {
        var fields = QueryExtensions.SortFields<Store>();
        fields["tradeName"] = s => s.TradeName;
        fields["city"] = s => s.City;
        fields["state"] = s => s.State;
        fields["createdAt"] = s => s.CreatedAt;
        fields["receiptCount"] = s => s.Receipts.Count(r => r.Status == ReceiptStatus.Confirmed);
        fields["totalSpent"] = s => s.Receipts.Where(r => r.Status == ReceiptStatus.Confirmed).Sum(r => r.Total);
        fields["lastPurchaseAt"] = s => s.Receipts.Where(r => r.Status == ReceiptStatus.Confirmed).Max(r => (DateTime?)r.IssuedAt);
        return fields;
    }

    private static NotFoundException NotFound(int id) => new($"Supermercado {id} não encontrado.");
}
