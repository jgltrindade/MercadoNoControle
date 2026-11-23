using System.Linq.Expressions;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Storage;
using MercadoNoControle.Api.Validators;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Receipts;

public interface IReceiptService
{
    Task<PagedResult<ReceiptListItem>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken);

    Task<ReceiptResponse> GetAsync(int id, CancellationToken cancellationToken);

    Task<ReceiptResponse> CreateAsync(ReceiptRequest request, CancellationToken cancellationToken);

    Task<ReceiptResponse> UpdateAsync(int id, ReceiptRequest request, CancellationToken cancellationToken);

    Task<ReceiptResponse> ConfirmAsync(int id, ConfirmReceiptRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);

    Task<ReceiptResponse> AttachImageAsync(int id, Stream content, long length, CancellationToken cancellationToken);

    Task<ReceiptResponse> RemoveImageAsync(int id, CancellationToken cancellationToken);

    Task<(Stream Content, string ContentType)> OpenImageAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Cria a compra já confirmada a partir de uma importação conferida pelo usuário, em uma única
    /// transação (nota, itens, histórico de preços, descrições aprendidas e vínculo da importação).
    /// </summary>
    Task<ReceiptResponse> CreateConfirmedFromImportAsync(
        ReceiptImport import, ReceiptRequest request, bool acknowledgeWarnings, CancellationToken cancellationToken);
}

/// <summary>
/// Compras (notas fiscais e itens). Uma nota nasce como rascunho e só participa de relatórios
/// e do histórico de preços após a confirmação explícita do usuário (RN01). Alterações são
/// gravadas em transação, mantendo itens, observações de preço e descrições alternativas coerentes.
/// </summary>
public sealed class ReceiptService(
    AppDbContext db,
    PriceObservationRecorder priceRecorder,
    IReceiptImageStorage imageStorage,
    TimeProvider timeProvider) : IReceiptService
{
    private static readonly Dictionary<string, Expression<Func<Receipt, object?>>> SortFields = BuildSortFields();

    public async Task<PagedResult<ReceiptListItem>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken)
    {
        var receipts = db.Receipts.AsNoTracking();

        if (TextNormalizer.Clean(query.Search) is { } search)
        {
            var pattern = QueryExtensions.ContainsPattern(search);
            var keyPattern = QueryExtensions.ContainsPattern(AccessKeyValidator.Normalize(search));
            const string escape = QueryExtensions.LikeEscape;
            receipts = receipts.Where(r =>
                EF.Functions.Like(r.Number, pattern, escape)
                || EF.Functions.Like(r.AccessKey, keyPattern, escape)
                || EF.Functions.Like(r.Store.TradeName, pattern, escape));
        }

        if (query.StoreId is { } storeId)
        {
            receipts = receipts.Where(r => r.StoreId == storeId);
        }

        if (query.Status is { } status)
        {
            receipts = receipts.Where(r => r.Status == status);
        }

        if (query is { From: { } from, To: { } to } && from > to)
        {
            throw new BusinessRuleException(
                "Período inválido.",
                new Dictionary<string, string[]> { ["to"] = ["A data final deve ser igual ou posterior à inicial."] });
        }

        if (query.From is { } fromDate)
        {
            var start = fromDate.ToDateTime(TimeOnly.MinValue);
            receipts = receipts.Where(r => r.IssuedAt >= start);
        }

        if (query.To is { } toDate)
        {
            var endExclusive = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
            receipts = receipts.Where(r => r.IssuedAt < endExclusive);
        }

        return await receipts
            .ApplySort(query, SortFields, "issuedAt", r => r.Id, defaultDescending: true)
            .Select(r => new ReceiptListItem(
                r.Id,
                r.StoreId,
                r.Store.TradeName,
                r.Number,
                r.Series,
                r.IssuedAt,
                r.Total,
                r.Items.Count,
                r.Items.Count(i => i.ProductId == null),
                r.Status,
                r.ImagePath != null,
                r.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<ReceiptResponse> GetAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts
            .AsNoTracking()
            .Include(r => r.Store)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw NotFound(id);

        return await ToResponseAsync(receipt, cancellationToken);
    }

    public async Task<ReceiptResponse> CreateAsync(ReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = new Receipt { Status = ReceiptStatus.Draft, CreatedAt = Now() };
        await ApplyAsync(receipt, request, cancellationToken);

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<ReceiptResponse> UpdateAsync(int id, ReceiptRequest request, CancellationToken cancellationToken)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var receipt = await LoadForChangeAsync(id, cancellationToken);
            await ApplyAsync(receipt, request, cancellationToken);
            receipt.UpdatedAt = Now();

            if (receipt.Status == ReceiptStatus.Confirmed)
            {
                if (receipt.Items.Count == 0)
                {
                    throw NoItemsError();
                }

                // RN04: dados derivados da compra confirmada acompanham a edição.
                await LearnAliasesAsync(receipt, cancellationToken);
                await priceRecorder.SyncAsync(receipt, Now(), cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ReceiptResponse> ConfirmAsync(int id, ConfirmReceiptRequest request, CancellationToken cancellationToken)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var receipt = await LoadForChangeAsync(id, cancellationToken);
            if (receipt.Status == ReceiptStatus.Confirmed)
            {
                return await GetAsync(id, cancellationToken);
            }

            if (receipt.Items.Count == 0)
            {
                throw NoItemsError();
            }

            var warnings = ReceiptConsistency.Check(receipt, receipt.Store.Cnpj);
            var duplicates = await FindPossibleDuplicatesAsync(receipt, cancellationToken);
            if ((warnings.Count > 0 || duplicates.Count > 0) && !request.AcknowledgeWarnings)
            {
                throw new ConflictException(
                    "A nota possui alertas. Revise-os e confirme novamente para registrar a compra.",
                    new Dictionary<string, object?>
                    {
                        ["warnings"] = warnings,
                        ["possibleDuplicates"] = duplicates
                    });
            }

            receipt.Status = ReceiptStatus.Confirmed;
            receipt.UpdatedAt = Now();
            await LearnAliasesAsync(receipt, cancellationToken);
            await priceRecorder.SyncAsync(receipt, Now(), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ReceiptResponse> CreateConfirmedFromImportAsync(
        ReceiptImport import, ReceiptRequest request, bool acknowledgeWarnings, CancellationToken cancellationToken)
    {
        int receiptId;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var receipt = new Receipt { Status = ReceiptStatus.Draft, CreatedAt = Now(), ImagePath = import.ImagePath };
            await ApplyAsync(receipt, request, cancellationToken);
            if (receipt.Items.Count == 0)
            {
                throw NoItemsError();
            }

            receipt.Store = await db.Stores.SingleAsync(s => s.Id == receipt.StoreId, cancellationToken);
            var warnings = ReceiptConsistency.Check(receipt, receipt.Store.Cnpj);
            var duplicates = await FindPossibleDuplicatesAsync(receipt, cancellationToken);
            if ((warnings.Count > 0 || duplicates.Count > 0) && !acknowledgeWarnings)
            {
                // Nada foi gravado: a compra só é registrada após a ciência do usuário.
                throw new ConflictException(
                    "A nota possui alertas. Revise-os e confirme novamente para registrar a compra.",
                    new Dictionary<string, object?>
                    {
                        ["warnings"] = warnings,
                        ["possibleDuplicates"] = duplicates
                    });
            }

            receipt.Status = ReceiptStatus.Confirmed;
            db.Receipts.Add(receipt);
            import.Receipt = receipt;
            import.Status = ReceiptImportStatus.Confirmed;
            await LearnAliasesAsync(receipt, cancellationToken);
            await priceRecorder.SyncAsync(receipt, Now(), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            receiptId = receipt.Id;
        }

        return await GetAsync(receiptId, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.FindAsync([id], cancellationToken) ?? throw NotFound(id);

        // RN05: itens e observações de preço são removidos em cascata pelo banco;
        // importações associadas são preservadas sem vínculo. A imagem é mantida em disco.
        db.Receipts.Remove(receipt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReceiptResponse> AttachImageAsync(int id, Stream content, long length, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.FindAsync([id], cancellationToken) ?? throw NotFound(id);

        var stored = await imageStorage.SaveAsync(content, length, cancellationToken);
        receipt.ImagePath = stored.RelativePath;
        receipt.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ReceiptResponse> RemoveImageAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.FindAsync([id], cancellationToken) ?? throw NotFound(id);

        // Somente desvincula: a imagem original é preservada no armazenamento.
        receipt.ImagePath = null;
        receipt.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)> OpenImageAsync(int id, CancellationToken cancellationToken)
    {
        var imagePath = await db.Receipts.Where(r => r.Id == id).Select(r => new { r.ImagePath }).SingleOrDefaultAsync(cancellationToken)
                        ?? throw NotFound(id);
        if (imagePath.ImagePath is null)
        {
            throw new NotFoundException("A nota não possui imagem.");
        }

        return (imageStorage.OpenRead(imagePath.ImagePath), imageStorage.ContentTypeOf(imagePath.ImagePath));
    }

    private async Task<Receipt> LoadForChangeAsync(int id, CancellationToken cancellationToken) =>
        await db.Receipts
            .Include(r => r.Store)
            .Include(r => r.Items).ThenInclude(i => i.PriceObservation)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw NotFound(id);

    /// <summary>Valida referências e aplica cabeçalho e itens (inclusão, alteração e remoção por Id).</summary>
    private async Task ApplyAsync(Receipt receipt, ReceiptRequest request, CancellationToken cancellationToken)
    {
        var storeId = request.StoreId!.Value;
        if (!await db.Stores.AnyAsync(s => s.Id == storeId, cancellationToken))
        {
            throw Invalid("storeId", "O supermercado informado não existe.");
        }

        var accessKey = TextNormalizer.Clean(request.AccessKey) is { } key ? AccessKeyValidator.Normalize(key) : null;
        if (accessKey is not null)
        {
            var existing = await db.Receipts.AsNoTracking()
                .Where(r => r.AccessKey == accessKey && r.Id != receipt.Id)
                .Select(r => new { r.Id, r.IssuedAt, r.Store.TradeName })
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                // RN02: a chave de acesso identifica a nota; a mesma nota não pode ser registrada duas vezes.
                throw new ConflictException(
                    $"Esta nota já está registrada (compra de {existing.IssuedAt:dd/MM/yyyy} em {existing.TradeName}).",
                    new Dictionary<string, object?> { ["existingReceiptId"] = existing.Id });
            }
        }

        await ValidateItemsAsync(receipt, request.Items, cancellationToken);

        receipt.StoreId = storeId;
        receipt.Number = TextNormalizer.Clean(request.Number);
        receipt.Series = TextNormalizer.Clean(request.Series);
        receipt.AccessKey = accessKey;
        receipt.IssuedAt = DateTime.SpecifyKind(request.IssuedAt!.Value, DateTimeKind.Unspecified);
        receipt.ProductsTotal = request.ProductsTotal;
        receipt.DiscountTotal = request.DiscountTotal;
        receipt.AdditionalCharges = request.AdditionalCharges;
        receipt.Total = request.Total;
        receipt.PaymentMethod = TextNormalizer.Clean(request.PaymentMethod);

        ApplyItems(receipt, request.Items);
    }

    private async Task ValidateItemsAsync(Receipt receipt, List<ReceiptItemRequest> items, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var existingIds = receipt.Items.Select(i => i.Id).ToHashSet();
        var seenIds = new HashSet<int>();

        var productIds = items.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value).Distinct().ToList();
        var knownProducts = productIds.Count == 0
            ? []
            : (await db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Id is { } itemId && (!existingIds.Contains(itemId) || !seenIds.Add(itemId)))
            {
                errors[$"items[{index}].id"] = ["Item inexistente nesta nota."];
            }

            if (item.ProductId is { } productId && !knownProducts.Contains(productId))
            {
                errors[$"items[{index}].productId"] = ["O produto informado não existe."];
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException("Itens inválidos.", errors);
        }
    }

    private void ApplyItems(Receipt receipt, List<ReceiptItemRequest> requested)
    {
        var keptIds = requested.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToHashSet();
        foreach (var removed in receipt.Items.Where(i => !keptIds.Contains(i.Id)).ToList())
        {
            receipt.Items.Remove(removed);
            db.ReceiptItems.Remove(removed);
        }

        var byId = receipt.Items.ToDictionary(i => i.Id);
        for (var index = 0; index < requested.Count; index++)
        {
            var source = requested[index];
            var item = source.Id is { } id ? byId[id] : new ReceiptItem { OriginalDescription = string.Empty };
            if (source.Id is null)
            {
                receipt.Items.Add(item);
            }

            item.LineNumber = index + 1;
            item.OriginalDescription = TextNormalizer.CollapseWhitespace(source.Description);
            item.ProductId = source.ProductId;
            item.Quantity = source.Quantity;
            item.Unit = TextNormalizer.Clean(source.Unit)?.ToUpperInvariant();
            item.UnitPrice = source.UnitPrice;
            item.Discount = source.Discount;
            item.Total = source.Total;
        }
    }

    /// <summary>
    /// Ao confirmar, cada associação item → produto feita pelo usuário passa a ser uma descrição
    /// alternativa confirmada do produto (origem: nota fiscal), usada para sugerir associações futuras.
    /// </summary>
    private async Task LearnAliasesAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var pairs = receipt.Items
            .Where(i => i.ProductId is not null)
            .Select(i => (ProductId: i.ProductId!.Value, Description: i.OriginalDescription,
                Normalized: TextNormalizer.NormalizeDescription(i.OriginalDescription)))
            .DistinctBy(p => (p.ProductId, p.Normalized))
            .ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        var productIds = pairs.Select(p => p.ProductId).Distinct().ToList();
        var normalized = pairs.Select(p => p.Normalized).Distinct().ToList();
        var existing = await db.ProductAliases
            .Where(a => productIds.Contains(a.ProductId) && normalized.Contains(a.NormalizedDescription))
            .ToListAsync(cancellationToken);

        foreach (var pair in pairs)
        {
            var alias = existing.FirstOrDefault(a => a.ProductId == pair.ProductId && a.NormalizedDescription == pair.Normalized);
            if (alias is null)
            {
                db.ProductAliases.Add(new ProductAlias
                {
                    ProductId = pair.ProductId,
                    Description = pair.Description,
                    NormalizedDescription = pair.Normalized,
                    Confirmed = true,
                    Source = AliasSource.Receipt,
                    CreatedAt = Now()
                });
            }
            else
            {
                alias.Confirmed = true;
            }
        }
    }

    /// <summary>
    /// RN02 (sem chave de acesso): notas confirmadas do mesmo supermercado com o mesmo número e série,
    /// ou na mesma data e com o mesmo valor total. É apenas um alerta — compras legítimas não são bloqueadas.
    /// </summary>
    private async Task<IReadOnlyList<DuplicateCandidate>> FindPossibleDuplicatesAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var dayStart = receipt.IssuedAt.Date;
        var dayEnd = dayStart.AddDays(1);
        var number = receipt.Number;
        var series = receipt.Series;
        var total = receipt.Total;

        var candidates = await db.Receipts.AsNoTracking()
            .Where(r => r.Id != receipt.Id && r.Status == ReceiptStatus.Confirmed && r.StoreId == receipt.StoreId)
            .Where(r => (number != null && r.Number == number && r.Series == series)
                        || (r.IssuedAt >= dayStart && r.IssuedAt < dayEnd && r.Total == total))
            .OrderByDescending(r => r.IssuedAt)
            .Take(10)
            .Select(r => new { r.Id, r.Store.TradeName, r.Number, r.Series, r.IssuedAt, r.Total })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(r => new DuplicateCandidate(
                r.Id,
                r.TradeName,
                r.Number,
                r.Series,
                r.IssuedAt,
                r.Total,
                number is not null && r.Number == number && r.Series == series
                    ? "Mesmo número e série"
                    : "Mesma data e mesmo valor total"))
            .ToList();
    }

    private async Task<ReceiptResponse> ToResponseAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var warnings = ReceiptConsistency.Check(receipt, receipt.Store.Cnpj);
        var duplicates = await FindPossibleDuplicatesAsync(receipt, cancellationToken);
        var items = receipt.Items
            .OrderBy(i => i.LineNumber)
            .Select(i => new ReceiptItemResponse(
                i.Id, i.LineNumber, i.OriginalDescription, i.ProductId, i.Product?.Name,
                i.Quantity, i.Unit, i.UnitPrice, i.Discount, i.Total))
            .ToList();

        return new ReceiptResponse(
            receipt.Id,
            receipt.StoreId,
            receipt.Store.TradeName,
            receipt.Store.Cnpj,
            receipt.Number,
            receipt.Series,
            receipt.AccessKey,
            receipt.IssuedAt,
            receipt.ProductsTotal,
            receipt.DiscountTotal,
            receipt.AdditionalCharges,
            receipt.Total,
            receipt.PaymentMethod,
            receipt.Status,
            receipt.ImagePath is not null,
            receipt.CreatedAt,
            receipt.UpdatedAt,
            items,
            warnings,
            duplicates);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static Dictionary<string, Expression<Func<Receipt, object?>>> BuildSortFields()
    {
        var fields = QueryExtensions.SortFields<Receipt>();
        fields["issuedAt"] = r => r.IssuedAt;
        fields["total"] = r => r.Total;
        fields["store"] = r => r.Store.TradeName;
        fields["status"] = r => r.Status;
        fields["createdAt"] = r => r.CreatedAt;
        return fields;
    }

    private static BusinessRuleException NoItemsError() =>
        Invalid("items", "Inclua ao menos um item antes de confirmar a compra.");

    private static BusinessRuleException Invalid(string field, string message) =>
        new(message, new Dictionary<string, string[]> { [field] = [message] });

    private static NotFoundException NotFound(int id) => new($"Nota fiscal {id} não encontrada.");
}
