using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Services.Imports;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

/// <summary>
/// Leitura de notas por imagem com IA. O upload devolve o identificador da importação; o status,
/// a conferência e a confirmação usam esse identificador, antes de existir uma nota fiscal.
/// </summary>
[ApiController]
[Route("api/receipts")]
public class ReceiptImportsController(IReceiptImportService importService) : ControllerBase
{
    private const long MaxUploadRequestBytes = 25 * 1024 * 1024;

    /// <summary>Indica se a leitura automática está disponível (sem ela, use o cadastro manual).</summary>
    [HttpGet("imports/reader")]
    public ReaderStatusResponse ReaderStatus() => importService.GetReaderStatus();

    /// <summary>
    /// Envia a fotografia da nota. A imagem é validada e armazenada, e a leitura ocorre em segundo
    /// plano; acompanhe pelo identificador retornado.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ReceiptImportResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var import = await importService.UploadAsync(content, file.Length, file.FileName, cancellationToken);
        return AcceptedAtAction(nameof(GetImport), new { importId = import.Id }, import);
    }

    /// <summary>Importações em aberto (em leitura, aguardando conferência ou com falha).</summary>
    [HttpGet("imports")]
    public Task<IReadOnlyList<ReceiptImportResponse>> ListImports(CancellationToken cancellationToken) =>
        importService.ListOpenAsync(cancellationToken);

    /// <summary>Status da importação e, quando lida, os dados sugeridos para conferência.</summary>
    [HttpGet("imports/{importId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ReceiptImportResponse> GetImport(Guid importId, CancellationToken cancellationToken) =>
        importService.GetAsync(importId, cancellationToken);

    [HttpGet("imports/{importId:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImportImage(Guid importId, CancellationToken cancellationToken)
    {
        var (content, contentType) = await importService.OpenImageAsync(importId, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, contentType);
    }

    /// <summary>Lê a imagem novamente (após falha ou para refazer a leitura).</summary>
    [HttpPost("imports/{importId:guid}/reprocess")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ReceiptImportResponse> Reprocess(Guid importId, CancellationToken cancellationToken) =>
        importService.ReprocessAsync(importId, cancellationToken);

    /// <summary>Descarta a importação sem registrar compra.</summary>
    [HttpPut("imports/{importId:guid}/discard")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ReceiptImportResponse> Discard(Guid importId, CancellationToken cancellationToken) =>
        importService.DiscardAsync(importId, cancellationToken);

    /// <summary>
    /// Registra a compra com os dados conferidos pelo usuário. Com alertas e sem
    /// <c>acknowledgeWarnings</c>, responde 409 com <c>warnings</c> e <c>possibleDuplicates</c> e nada é gravado.
    /// </summary>
    [HttpPut("imports/{importId:guid}/confirm")]
    [ProducesResponseType<ReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ReceiptResponse> Confirm(Guid importId, ConfirmImportRequest request, CancellationToken cancellationToken) =>
        importService.ConfirmAsync(importId, request, cancellationToken);

    /// <summary>Registros de processamento (importações) associados a uma nota confirmada.</summary>
    [HttpGet("{id:int}/processing")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ReceiptImportResponse>> Processing(int id, CancellationToken cancellationToken) =>
        importService.ListForReceiptAsync(id, cancellationToken);
}
