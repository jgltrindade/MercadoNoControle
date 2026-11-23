using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Receipts;
using MercadoNoControle.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Imports;

public interface IReceiptImportService
{
    ReaderStatusResponse GetReaderStatus();

    Task<ReceiptImportResponse> UploadAsync(Stream content, long length, string? fileName, CancellationToken cancellationToken);

    Task<ReceiptImportResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptImportResponse>> ListOpenAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptImportResponse>> ListForReceiptAsync(int receiptId, CancellationToken cancellationToken);

    Task<ReceiptImportResponse> ReprocessAsync(Guid id, CancellationToken cancellationToken);

    Task<ReceiptImportResponse> DiscardAsync(Guid id, CancellationToken cancellationToken);

    Task<ReceiptResponse> ConfirmAsync(Guid id, ConfirmImportRequest request, CancellationToken cancellationToken);

    Task<(Stream Content, string ContentType)> OpenImageAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Importação de notas por imagem (especificação 5.2). A importação tem identidade e estado
/// próprios; a nota fiscal só é criada quando o usuário confirma os dados conferidos (RN01).
/// </summary>
public sealed class ReceiptImportService(
    AppDbContext db,
    IReceiptReader reader,
    IReceiptImageStorage imageStorage,
    ReceiptImportQueue queue,
    ReceiptDraftBuilder draftBuilder,
    IReceiptService receiptService,
    TimeProvider timeProvider) : IReceiptImportService
{
    private const int OpenImportsLimit = 20;

    private static readonly ReceiptImportStatus[] OpenStatuses =
    [
        ReceiptImportStatus.Pending,
        ReceiptImportStatus.Processing,
        ReceiptImportStatus.AwaitingReview,
        ReceiptImportStatus.Failed
    ];

    public ReaderStatusResponse GetReaderStatus() => new(
        reader.IsAvailable,
        reader.ProviderName,
        reader.ModelName,
        reader is UnavailableReceiptReader unavailable ? unavailable.Reason : null);

    public async Task<ReceiptImportResponse> UploadAsync(Stream content, long length, string? fileName, CancellationToken cancellationToken)
    {
        var stored = await imageStorage.SaveAsync(content, length, cancellationToken);
        var import = new ReceiptImport
        {
            Id = Guid.NewGuid(),
            Status = ReceiptImportStatus.Pending,
            ImagePath = stored.RelativePath,
            ContentType = stored.ContentType,
            FileSize = stored.Size,
            OriginalFileName = SafeFileName(fileName),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        db.ReceiptImports.Add(import);
        await db.SaveChangesAsync(cancellationToken);
        queue.Enqueue(import.Id);

        return await ToResponseAsync(import, cancellationToken);
    }

    public async Task<ReceiptImportResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ToResponseAsync(await FindAsync(id, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<ReceiptImportResponse>> ListOpenAsync(CancellationToken cancellationToken)
    {
        var imports = await db.ReceiptImports.AsNoTracking()
            .Where(i => OpenStatuses.Contains(i.Status))
            .OrderByDescending(i => i.CreatedAt)
            .Take(OpenImportsLimit)
            .ToListAsync(cancellationToken);

        // A listagem não inclui o rascunho (é montado ao abrir a importação).
        return imports.Select(i => ToResponse(i, draft: null)).ToList();
    }

    public async Task<IReadOnlyList<ReceiptImportResponse>> ListForReceiptAsync(int receiptId, CancellationToken cancellationToken)
    {
        if (!await db.Receipts.AnyAsync(r => r.Id == receiptId, cancellationToken))
        {
            throw new NotFoundException($"Nota fiscal {receiptId} não encontrada.");
        }

        var imports = await db.ReceiptImports.AsNoTracking()
            .Where(i => i.ReceiptId == receiptId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        return imports.Select(i => ToResponse(i, draft: null)).ToList();
    }

    public async Task<ReceiptImportResponse> ReprocessAsync(Guid id, CancellationToken cancellationToken)
    {
        var import = await FindAsync(id, cancellationToken);
        if (import.Status is not (ReceiptImportStatus.Failed or ReceiptImportStatus.AwaitingReview))
        {
            throw new ConflictException("Somente importações com falha ou aguardando conferência podem ser lidas novamente.");
        }

        import.Status = ReceiptImportStatus.Pending;
        import.Error = null;
        await db.SaveChangesAsync(cancellationToken);
        queue.Enqueue(import.Id);

        return await ToResponseAsync(import, cancellationToken);
    }

    public async Task<ReceiptImportResponse> DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        var import = await FindAsync(id, cancellationToken);
        if (import.Status is ReceiptImportStatus.Confirmed)
        {
            throw new ConflictException("A importação já gerou uma compra e não pode ser descartada.");
        }

        import.Status = ReceiptImportStatus.Discarded;
        await db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(import, cancellationToken);
    }

    public async Task<ReceiptResponse> ConfirmAsync(Guid id, ConfirmImportRequest request, CancellationToken cancellationToken)
    {
        var import = await FindAsync(id, cancellationToken);
        if (import.Status is not (ReceiptImportStatus.AwaitingReview or ReceiptImportStatus.Failed))
        {
            throw new ConflictException(import.Status switch
            {
                ReceiptImportStatus.Confirmed => "Esta importação já foi confirmada.",
                ReceiptImportStatus.Discarded => "Esta importação foi descartada.",
                _ => "A leitura ainda está em andamento. Aguarde a conclusão."
            });
        }

        // A imagem da importação acompanha a compra; leitura com falha pode ser completada manualmente.
        return await receiptService.CreateConfirmedFromImportAsync(import, request, request.AcknowledgeWarnings, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)> OpenImageAsync(Guid id, CancellationToken cancellationToken)
    {
        var import = await FindAsync(id, cancellationToken);
        return (imageStorage.OpenRead(import.ImagePath), import.ContentType);
    }

    private async Task<ReceiptImport> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ReceiptImports.FindAsync([id], cancellationToken)
        ?? throw new NotFoundException("Importação não encontrada.");

    private async Task<ReceiptImportResponse> ToResponseAsync(ReceiptImport import, CancellationToken cancellationToken)
    {
        var draft = import.Status is ReceiptImportStatus.AwaitingReview or ReceiptImportStatus.Failed
            ? await draftBuilder.BuildAsync(import.ResultJson, cancellationToken)
            : null;
        return ToResponse(import, draft);
    }

    private static ReceiptImportResponse ToResponse(ReceiptImport import, ReceiptDraft? draft) => new(
        import.Id,
        import.Status,
        import.OriginalFileName,
        import.FileSize,
        import.Provider,
        import.Model,
        import.Error,
        import.AttemptCount,
        import.CreatedAt,
        import.ProcessedAt,
        import.ReceiptId,
        draft);

    private static string? SafeFileName(string? fileName)
    {
        var name = TextNormalizer.Clean(Path.GetFileName(fileName ?? string.Empty));
        return name is null ? null : name.Length <= 260 ? name : name[..260];
    }
}
