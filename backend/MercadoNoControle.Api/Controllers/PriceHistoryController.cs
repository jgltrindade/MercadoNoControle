using MercadoNoControle.Api.DTOs.PriceHistory;
using MercadoNoControle.Api.Services.PriceHistory;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

[ApiController]
public class PriceHistoryController(IPriceHistoryService priceHistoryService) : ControllerBase
{
    /// <summary>
    /// Histórico de preços do produto: compras (observações) e indicadores — último, menor, maior
    /// e médio preço, variação no período e, quando aplicável, preço normalizado por kg/L/unidade.
    /// </summary>
    [HttpGet("api/products/{id:int}/prices")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ProductPriceHistoryResponse> Prices(int id, [FromQuery] PriceHistoryQuery query, CancellationToken cancellationToken) =>
        priceHistoryService.GetProductPricesAsync(id, query, cancellationToken);

    /// <summary>
    /// Comparação do produto entre supermercados e, para referência, produtos da mesma categoria
    /// com preço normalizado na mesma unidade.
    /// </summary>
    [HttpGet("api/products/{id:int}/comparison")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ProductPriceComparisonResponse> Comparison(int id, [FromQuery] PriceHistoryQuery query, CancellationToken cancellationToken) =>
        priceHistoryService.GetComparisonAsync(id, query, cancellationToken);

    /// <summary>Itens de compras confirmadas sem produto associado, agrupados por descrição.</summary>
    [HttpGet("api/price-history/unassigned-items")]
    public Task<IReadOnlyList<UnassignedItemGroup>> UnassignedItems(CancellationToken cancellationToken) =>
        priceHistoryService.ListUnassignedAsync(cancellationToken);

    /// <summary>
    /// Associa ao produto todos os itens sem produto com a descrição informada (RN06), gerando o
    /// histórico de preços desses itens e registrando a descrição alternativa.
    /// </summary>
    [HttpPost("api/price-history/assign")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<AssignItemsResponse> Assign(AssignItemsRequest request, CancellationToken cancellationToken) =>
        priceHistoryService.AssignAsync(request, cancellationToken);
}
