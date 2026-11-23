using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Api;

/// <summary>Fluxo HTTP de compras (Fase 3) com a API e o SQLite reais.</summary>
public class ReceiptsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<int> CreateStoreAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/stores", new { tradeName = name });
        return (await response.Content.ReadFromJsonAsync<StoreResponse>(Json))!.Id;
    }

    private async Task<int> CreateProductAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/products", new { name, unit = "UN" });
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(Json))!.Id;
    }

    private static object ReceiptBody(int storeId, int? productId, decimal total = 12.00m) => new
    {
        storeId,
        issuedAt = "2026-11-15T14:30:00",
        productsTotal = 12.00m,
        discountTotal = 0m,
        additionalCharges = 0m,
        total,
        items = new[]
        {
            new { description = "CAFE TORRADO 500G", productId, quantity = 1m, unit = "UN", unitPrice = 12.00m, discount = 0m, total = 12.00m }
        }
    };

    [Fact]
    public async Task Manual_purchase_flow_with_warning_acknowledgement()
    {
        var storeId = await CreateStoreAsync("Loja Fluxo HTTP");
        var productId = await CreateProductAsync("Café 500g HTTP");

        var create = await _client.PostAsJsonAsync("/api/receipts", ReceiptBody(storeId, productId, total: 15.00m));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var draft = (await create.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!;
        Assert.Equal(ReceiptStatus.Draft, draft.Status);
        Assert.Equal(new DateTime(2026, 11, 15, 14, 30, 0), draft.IssuedAt);
        Assert.Contains(draft.Warnings, w => w.Code == "TOTAL_MISMATCH");

        var withoutAck = await _client.PutAsJsonAsync($"/api/receipts/{draft.Id}/confirm", new { });
        Assert.Equal(HttpStatusCode.Conflict, withoutAck.StatusCode);
        var problem = await withoutAck.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TOTAL_MISMATCH", problem.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Equal(0, problem.GetProperty("possibleDuplicates").GetArrayLength());

        var confirm = await _client.PutAsJsonAsync($"/api/receipts/{draft.Id}/confirm", new { acknowledgeWarnings = true });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(ReceiptStatus.Confirmed, (await confirm.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!.Status);

        var matches = await _client.GetFromJsonAsync<List<ProductMatch>>("/api/products/match?description=caf%C3%A9%20torrado%20500g", Json);
        Assert.Equal(productId, Assert.Single(matches!).ProductId);

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/receipts?storeId={storeId}&status=Confirmed");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/receipts/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/receipts/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Item_validation_errors_point_to_the_item_field()
    {
        var response = await _client.PostAsJsonAsync("/api/receipts", new
        {
            issuedAt = "2026-11-15T14:30:00",
            accessKey = "123",
            items = new[] { new { description = "", quantity = 0m, unitPrice = 1.12345m, discount = 0m, total = 0m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("storeId", out _));
        Assert.True(errors.TryGetProperty("accessKey", out _));
        Assert.True(errors.TryGetProperty("items[0].description", out _));
        Assert.True(errors.TryGetProperty("items[0].quantity", out _));
        Assert.True(errors.TryGetProperty("items[0].unitPrice", out _));
    }

    [Fact]
    public async Task Image_upload_validates_content_and_serves_the_original()
    {
        var storeId = await CreateStoreAsync("Loja Imagem HTTP");
        var created = await _client.PostAsJsonAsync("/api/receipts", ReceiptBody(storeId, null));
        var receiptId = (await created.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!.Id;

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3, 4, 5, 6, 7, 8];
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "nota.jpg");

        var upload = await _client.PostAsync($"/api/receipts/{receiptId}/image", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.True((await upload.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!.HasImage);

        var image = await _client.GetAsync($"/api/receipts/{receiptId}/image");
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(jpeg, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());

        using var fake = new MultipartFormDataContent();
        var html = new ByteArrayContent("<html></html>"u8.ToArray());
        html.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fake.Add(html, "file", "nota.png");
        var rejected = await _client.PostAsync($"/api/receipts/{receiptId}/image", fake);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var missingFile = await _client.PostAsync($"/api/receipts/{receiptId}/image", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, missingFile.StatusCode);
    }
}
