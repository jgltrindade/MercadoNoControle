using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.PriceHistory;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Api;

/// <summary>Consulta de histórico ponta a ponta: compra confirmada → histórico → comparação → associação posterior.</summary>
public class PriceHistoryEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task History_comparison_and_later_association()
    {
        var store = await PostAsync<StoreResponse>("/api/stores", new { tradeName = "Loja Historico HTTP" });
        var coffee = await PostAsync<ProductResponse>("/api/products", new { name = "Café 500g HTTP", unit = "G", packageContent = 500 });
        var sugar = await PostAsync<ProductResponse>("/api/products", new { name = "Açúcar 1kg HTTP", unit = "KG", packageContent = 1 });

        var receipt = await PostAsync<ReceiptResponse>("/api/receipts", new
        {
            storeId = store.Id,
            issuedAt = "2026-11-15T10:00:00",
            productsTotal = 22.00m,
            total = 22.00m,
            items = new object[]
            {
                new { description = "CAFE 500G HTTP", productId = coffee.Id, quantity = 1m, unit = "UN", unitPrice = 17.00m, total = 17.00m },
                new { description = "ACUCAR REF HTTP", quantity = 1m, unit = "UN", unitPrice = 5.00m, total = 5.00m }
            }
        });
        (await _client.PutAsJsonAsync($"/api/receipts/{receipt.Id}/confirm", new { acknowledgeWarnings = true })).EnsureSuccessStatusCode();

        var history = await _client.GetFromJsonAsync<ProductPriceHistoryResponse>($"/api/products/{coffee.Id}/prices", Json);
        Assert.Equal(1, history!.Summary.Count);
        Assert.Equal(34.0000m, history.Summary.LastNormalized); // R$ 17,00 por 500 g = R$ 34,00/kg
        Assert.Equal(NormalizedUnit.Kilogram, history.NormalizedUnit);

        var comparison = await _client.GetFromJsonAsync<ProductPriceComparisonResponse>($"/api/products/{coffee.Id}/comparison", Json);
        Assert.Equal("Loja Historico HTTP", Assert.Single(comparison!.Stores).StoreName);

        var unassigned = await _client.GetFromJsonAsync<List<UnassignedItemGroup>>("/api/price-history/unassigned-items", Json);
        Assert.Contains(unassigned!, g => g.NormalizedDescription == "ACUCAR REF HTTP");

        var assigned = await PostAsync<AssignItemsResponse>("/api/price-history/assign", new { description = "açúcar ref http", productId = sugar.Id });
        Assert.Equal(1, assigned.UpdatedItems);
        var sugarHistory = await _client.GetFromJsonAsync<ProductPriceHistoryResponse>($"/api/products/{sugar.Id}/prices", Json);
        Assert.Equal(5.0000m, sugarHistory!.Summary.LastNormalized);
    }

    [Fact]
    public async Task Invalid_requests_return_problem_details()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/products/99999/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/products/99999/comparison")).StatusCode);

        var product = await PostAsync<ProductResponse>("/api/products", new { name = "Produto Periodo HTTP", unit = "UN" });
        var invalidPeriod = await _client.GetAsync($"/api/products/{product.Id}/prices?from=2026-12-01&to=2026-11-01");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidPeriod.StatusCode);

        var missingProduct = await _client.PostAsJsonAsync("/api/price-history/assign", new { description = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, missingProduct.StatusCode);
    }
}
