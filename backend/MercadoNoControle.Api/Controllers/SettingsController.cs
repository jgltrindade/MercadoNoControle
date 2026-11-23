using MercadoNoControle.Api.DTOs.Settings;
using MercadoNoControle.Api.Services.Settings;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

/// <summary>Configurações (especificação 4.8): instalação, backup, exportação e imagens.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(ISettingsService settingsService, IBackupService backupService) : ControllerBase
{
    /// <summary>Versão, caminhos de armazenamento, volume de dados e leitura por IA (sem expor a chave).</summary>
    [HttpGet("system")]
    public Task<SystemInfoResponse> System(CancellationToken cancellationToken) =>
        settingsService.GetSystemInfoAsync(cancellationToken);

    /// <summary>Backup completo (banco + imagens) em .zip, restaurável conforme o README.</summary>
    [HttpGet("backup")]
    [Produces("application/zip")]
    public async Task<IActionResult> Backup(CancellationToken cancellationToken)
    {
        var archive = await backupService.CreateBackupAsync(cancellationToken);
        return File(archive.Content, "application/zip", archive.FileName);
    }

    /// <summary>Todos os dados em CSV (supermercados, categorias, produtos, compras, itens e histórico de preços).</summary>
    [HttpGet("export")]
    [Produces("application/zip")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var archive = await backupService.ExportDataAsync(cancellationToken);
        return File(archive.Content, "application/zip", archive.FileName);
    }

    /// <summary>Imagens armazenadas e imagens órfãs (sem nota nem leitura em andamento).</summary>
    [HttpGet("images")]
    public Task<ImagesSummaryResponse> Images(CancellationToken cancellationToken) =>
        settingsService.GetImagesAsync(cancellationToken);

    /// <summary>Exclui definitivamente as imagens órfãs.</summary>
    [HttpDelete("images/orphans")]
    public Task<DeleteOrphansResponse> DeleteOrphans(CancellationToken cancellationToken) =>
        settingsService.DeleteOrphanImagesAsync(cancellationToken);
}
