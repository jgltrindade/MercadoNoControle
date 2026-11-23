using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

[ApiController]
[Route("api/stores")]
public class StoresController(IStoreService storeService) : ControllerBase
{
    /// <summary>
    /// Lista supermercados com paginação. Busca por nome fantasia, razão social, cidade ou CNPJ.
    /// Ordenação: tradeName, city, state, createdAt, receiptCount, totalSpent, lastPurchaseAt.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<StoreResponse>> List([FromQuery] StoreQuery query, CancellationToken cancellationToken) =>
        storeService.ListAsync(query, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StoreResponse> Get(int id, CancellationToken cancellationToken) =>
        storeService.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<StoreResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StoreResponse>> Create(StoreRequest request, CancellationToken cancellationToken)
    {
        var store = await storeService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = store.Id }, store);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<StoreResponse> Update(int id, StoreRequest request, CancellationToken cancellationToken) =>
        storeService.UpdateAsync(id, request, cancellationToken);

    /// <summary>Exclui um supermercado sem compras registradas.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await storeService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
