using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(IProductService productService) : ControllerBase
{
    /// <summary>
    /// Lista produtos com paginação. Busca por nome, marca, código de barras ou descrição alternativa.
    /// Ordenação: name, brand, category, unit, status, createdAt.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ProductListItem>> List([FromQuery] ProductQuery query, CancellationToken cancellationToken) =>
        productService.ListAsync(query, cancellationToken);

    /// <summary>Produtos associados a uma descrição de nota fiscal (sugestão de associação de itens).</summary>
    [HttpGet("match")]
    public Task<IReadOnlyList<ProductMatch>> Match([FromQuery] string description, CancellationToken cancellationToken) =>
        productService.MatchAsync(description, cancellationToken);

    /// <summary>Detalhes do produto, incluindo descrições alternativas.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProductResponse> Get(int id, CancellationToken cancellationToken) =>
        productService.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ProductResponse> Update(int id, ProductRequest request, CancellationToken cancellationToken) =>
        productService.UpdateAsync(id, request, cancellationToken);

    /// <summary>Exclui um produto que não esteja associado a compras.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Associa uma descrição de nota ao produto (confirmada pelo usuário).</summary>
    [HttpPost("{id:int}/aliases")]
    [ProducesResponseType<ProductAliasResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductAliasResponse>> AddAlias(int id, ProductAliasRequest request, CancellationToken cancellationToken)
    {
        var alias = await productService.AddAliasAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, alias);
    }

    /// <summary>Confirma uma associação sugerida (ex.: pela IA).</summary>
    [HttpPut("{id:int}/aliases/{aliasId:int}/confirm")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProductAliasResponse> ConfirmAlias(int id, int aliasId, CancellationToken cancellationToken) =>
        productService.ConfirmAliasAsync(id, aliasId, cancellationToken);

    [HttpDelete("{id:int}/aliases/{aliasId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAlias(int id, int aliasId, CancellationToken cancellationToken)
    {
        await productService.DeleteAliasAsync(id, aliasId, cancellationToken);
        return NoContent();
    }
}
