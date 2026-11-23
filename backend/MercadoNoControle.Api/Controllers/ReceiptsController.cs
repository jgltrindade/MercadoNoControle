using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.Services.Receipts;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

[ApiController]
[Route("api/receipts")]
public class ReceiptsController(IReceiptService receiptService) : ControllerBase
{
    /// <summary>Limite da requisição de upload (a regra de tamanho da imagem é validada no serviço).</summary>
    private const long MaxUploadRequestBytes = 25 * 1024 * 1024;

    /// <summary>
    /// Lista notas com paginação. Busca por número, chave de acesso ou supermercado; filtros por
    /// supermercado, situação e período (from/to, inclusive). Ordenação: issuedAt (padrão, desc),
    /// total, store, status, createdAt.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ReceiptListItem>> List([FromQuery] ReceiptQuery query, CancellationToken cancellationToken) =>
        receiptService.ListAsync(query, cancellationToken);

    /// <summary>Detalhes da nota com itens, alertas de consistência e possíveis duplicatas.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ReceiptResponse> Get(int id, CancellationToken cancellationToken) =>
        receiptService.GetAsync(id, cancellationToken);

    /// <summary>Cadastra uma compra manualmente, como rascunho (ainda não participa dos relatórios).</summary>
    [HttpPost]
    [ProducesResponseType<ReceiptResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReceiptResponse>> Create(ReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await receiptService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = receipt.Id }, receipt);
    }

    /// <summary>Atualiza a nota e seus itens. Em notas confirmadas, o histórico de preços é atualizado.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ReceiptResponse> Update(int id, ReceiptRequest request, CancellationToken cancellationToken) =>
        receiptService.UpdateAsync(id, request, cancellationToken);

    /// <summary>
    /// Confirma a compra. Se houver alertas (divergência de totais ou possível duplicidade) e
    /// <c>acknowledgeWarnings</c> for falso, responde 409 com <c>warnings</c> e <c>possibleDuplicates</c>.
    /// </summary>
    [HttpPut("{id:int}/confirm")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ReceiptResponse> Confirm(int id, ConfirmReceiptRequest? request, CancellationToken cancellationToken) =>
        receiptService.ConfirmAsync(id, request ?? new ConfirmReceiptRequest(), cancellationToken);

    /// <summary>Exclui a nota, seus itens e as observações de preço correspondentes.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await receiptService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Anexa (ou substitui) a imagem da nota. Aceita JPEG, PNG ou WebP.</summary>
    [HttpPost("{id:int}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ReceiptResponse> UploadImage(int id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await receiptService.AttachImageAsync(id, content, file.Length, cancellationToken);
    }

    /// <summary>Imagem original da nota.</summary>
    [HttpGet("{id:int}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
    {
        var (content, contentType) = await receiptService.OpenImageAsync(id, cancellationToken);
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, contentType);
    }

    /// <summary>Desvincula a imagem da nota (o arquivo original é preservado).</summary>
    [HttpDelete("{id:int}/image")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ReceiptResponse> RemoveImage(int id, CancellationToken cancellationToken) =>
        receiptService.RemoveImageAsync(id, cancellationToken);
}
