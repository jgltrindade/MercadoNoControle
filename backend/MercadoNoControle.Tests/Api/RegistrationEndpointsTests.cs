using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MercadoNoControle.Api.DTOs.Categories;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Api;

/// <summary>Testes HTTP de ponta a ponta dos cadastros (Fase 2), com a API e o SQLite reais.</summary>
public class RegistrationEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private readonly HttpClient _client = factory.CreateClient();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Store_crud_flow()
    {
        var create = await _client.PostAsJsonAsync("/api/stores", new { tradeName = "Mercado Fluxo", cnpj = "12.345.678/0001-95", state = "mg" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var store = (await create.Content.ReadFromJsonAsync<StoreResponse>(Json))!;
        Assert.Equal("12345678000195", store.Cnpj);
        Assert.Equal("MG", store.State);
        Assert.NotNull(create.Headers.Location);

        var duplicate = await _client.PostAsJsonAsync("/api/stores", new { tradeName = "Outro", cnpj = "12345678000195" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("Mercado Fluxo", (await ReadProblemAsync(duplicate)).GetProperty("detail").GetString());

        var update = await _client.PutAsJsonAsync($"/api/stores/{store.Id}", new { tradeName = "Mercado Fluxo 2", cnpj = "12345678000195" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var list = await _client.GetFromJsonAsync<PagedResult<StoreResponse>>("/api/stores?search=fluxo&pageSize=5", Json);
        Assert.Equal("Mercado Fluxo 2", Assert.Single(list!.Items).TradeName);
        Assert.Equal(5, list.PageSize);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/stores/{store.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/stores/{store.Id}")).StatusCode);
    }

    [Fact]
    public async Task Store_validation_errors_use_camel_case_fields()
    {
        var response = await _client.PostAsJsonAsync("/api/stores", new { tradeName = " ", cnpj = "11222333000182", state = "XX" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("tradeName", out _));
        Assert.True(errors.TryGetProperty("cnpj", out _));
        Assert.True(errors.TryGetProperty("state", out _));
    }

    [Theory]
    [InlineData("/api/stores?page=0")]
    [InlineData("/api/stores?pageSize=101")]
    [InlineData("/api/stores?sortDirection=up")]
    public async Task Invalid_paging_parameters_return_bad_request(string url)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Unknown_sort_field_returns_unprocessable_entity()
    {
        var response = await _client.GetAsync("/api/products?sortBy=secret");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("sortBy", out _));
    }

    [Fact]
    public async Task Category_crud_flow_and_delete_protection()
    {
        var create = await _client.PostAsJsonAsync("/api/categories", new { name = "Utilidades HTTP" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var category = (await create.Content.ReadFromJsonAsync<CategoryResponse>(Json))!;

        var product = await _client.PostAsJsonAsync("/api/products", new { name = "Vassoura HTTP", unit = "UN", categoryId = category.Id });
        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
        var productId = (await product.Content.ReadFromJsonAsync<ProductResponse>(Json))!.Id;

        var blocked = await _client.DeleteAsync($"/api/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/products/{productId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);

        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories", Json);
        Assert.DoesNotContain(categories!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task Product_validation_rejects_invalid_barcode_unit_and_excess_decimals()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "Inválido",
            barcode = "4006381333932",
            packageContent = 1.12345m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("barcode", out _));
        Assert.True(errors.TryGetProperty("unit", out _));
        Assert.True(errors.TryGetProperty("packageContent", out _));
    }

    [Fact]
    public async Task Product_with_unknown_unit_value_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new { name = "X", unit = "TONELADA" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Product_with_missing_category_returns_unprocessable_entity()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new { name = "Sem categoria", unit = "UN", categoryId = 99999 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("categoryId", out _));
    }

    [Fact]
    public async Task Product_alias_flow()
    {
        var create = await _client.PostAsJsonAsync("/api/products", new { name = "Leite Integral 1L HTTP", unit = "L", packageContent = 1 });
        var product = (await create.Content.ReadFromJsonAsync<ProductResponse>(Json))!;

        var alias = await _client.PostAsJsonAsync($"/api/products/{product.Id}/aliases", new { description = "LEITE INT 1L HTTP" });
        Assert.Equal(HttpStatusCode.Created, alias.StatusCode);
        var aliasId = (await alias.Content.ReadFromJsonAsync<ProductAliasResponse>(Json))!.Id;

        var duplicate = await _client.PostAsJsonAsync($"/api/products/{product.Id}/aliases", new { description = "leite int 1l http" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var details = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}", Json);
        Assert.Equal("LEITE INT 1L HTTP", Assert.Single(details!.Aliases).Description);

        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsync($"/api/products/{product.Id}/aliases/{aliasId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/products/{product.Id}/aliases/{aliasId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/products/{product.Id}/aliases/{aliasId}")).StatusCode);
    }
}
