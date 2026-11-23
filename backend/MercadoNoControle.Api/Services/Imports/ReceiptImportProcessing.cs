using System.Threading.Channels;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services.Imports;

/// <summary>Fila em memória das importações a processar (uma por vez, em segundo plano).</summary>
public sealed class ReceiptImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid importId) => _channel.Writer.TryWrite(importId);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Executa a leitura de uma importação e registra o resultado (estado, provedor, JSON, erro).</summary>
public sealed class ReceiptImportProcessor(
    AppDbContext db,
    IReceiptReader reader,
    IReceiptImageStorage imageStorage,
    TimeProvider timeProvider,
    ILogger<ReceiptImportProcessor> logger)
{
    public async Task ProcessAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await db.ReceiptImports.FindAsync([importId], cancellationToken);
        if (import is null || import.Status != ReceiptImportStatus.Pending)
        {
            return;
        }

        import.Status = ReceiptImportStatus.Processing;
        import.AttemptCount++;
        import.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
        import.Provider = reader.ProviderName;
        import.Model = reader.ModelName;
        import.Error = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await using var image = imageStorage.OpenRead(import.ImagePath);
            var result = await reader.ExtractAsync(image, cancellationToken);

            import.Provider = result.Provider;
            import.Model = result.Model ?? import.Model;
            import.ResultJson = result.RawJson;
            if (result.Extraction.IsReceipt)
            {
                import.Status = ReceiptImportStatus.AwaitingReview;
            }
            else
            {
                import.Status = ReceiptImportStatus.Failed;
                import.Error = Truncate(
                    $"A imagem não pôde ser lida como nota fiscal: {result.Extraction.UnreadableReason ?? "motivo não informado"}.");
            }
        }
        catch (ReceiptReaderException ex)
        {
            import.Status = ReceiptImportStatus.Failed;
            import.Error = Truncate(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Aplicação encerrando: a importação volta para a fila na próxima inicialização.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha inesperada ao processar a importação {ImportId}.", importId);
            import.Status = ReceiptImportStatus.Failed;
            import.Error = "Falha inesperada na leitura. Tente novamente ou cadastre manualmente.";
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Truncate(string message) => message.Length <= 2000 ? message : message[..2000];
}

/// <summary>
/// Serviço em segundo plano que consome a fila. Na inicialização, devolve à fila as importações
/// que ficaram pendentes ou interrompidas (ex.: aplicação encerrada durante a leitura).
/// </summary>
public sealed class ReceiptImportWorker(
    IServiceScopeFactory scopeFactory,
    ReceiptImportQueue queue,
    ILogger<ReceiptImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var importId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReceiptImportProcessor>().ProcessAsync(importId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar a importação {ImportId}.", importId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.ReceiptImports
            .Where(i => i.Status == ReceiptImportStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, ReceiptImportStatus.Pending), cancellationToken);

        var pending = await db.ReceiptImports
            .Where(i => i.Status == ReceiptImportStatus.Pending)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in pending)
        {
            queue.Enqueue(id);
        }

        if (pending.Count > 0)
        {
            logger.LogInformation("{Count} importação(ões) pendente(s) devolvida(s) à fila.", pending.Count);
        }
    }
}
