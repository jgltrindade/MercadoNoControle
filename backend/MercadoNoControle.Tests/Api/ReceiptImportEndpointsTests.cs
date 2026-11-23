using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MercadoNoControle.Tests.Api;

/// <summary>
/// Fluxo ponta a ponta da leitura por imagem (especificação 11, integração): enviar imagem,
/// processar em segundo plano, conferir, confirmar e verificar a persistência.
/// Usa o leitor simulado no lugar do provedor real.
/// </summary>
public class ReceiptImportEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly FakeReceiptReader _reader = new();

    private HttpClient CreateClient(IReceiptReader? reader = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(reader ?? _reader)))
            .CreateClient();

    private static MultipartFormDataContent ImageForm(byte[] bytes, string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "nota.jpg");
        return form;
    }

    private static async Task<ReceiptImportResponse> WaitForAsync(HttpClient client, Guid id, ReceiptImportStatus expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var import = (await client.GetFromJsonAsync<ReceiptImportResponse>($"/api/receipts/imports/{id}", Json))!;
            if (import.Status == expected)
            {
                return import;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"A importação não chegou ao estado {expected}.");
    }

    [Fact]
    public async Task Upload_process_review_and_confirm()
    {
        var client = CreateClient();
        var store = await client.PostAsJsonAsync("/api/stores", new { tradeName = "Loja Importacao", cnpj = "11222333000181" });
        var storeId = (await store.Content.ReadFromJsonAsync<StoreResponse>(Json))!.Id;

        var status = await client.GetFromJsonAsync<ReaderStatusResponse>("/api/receipts/imports/reader", Json);
        Assert.True(status!.Available);

        var upload = await client.PostAsync("/api/receipts/upload", ImageForm(FakeReceiptReader.JpegBytes));
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var created = (await upload.Content.ReadFromJsonAsync<ReceiptImportResponse>(Json))!;
        Assert.Equal(upload.Headers.Location!.AbsolutePath, $"/api/receipts/imports/{created.Id}");

        var review = await WaitForAsync(client, created.Id, ReceiptImportStatus.AwaitingReview);
        Assert.Equal(storeId, review.Draft!.StoreId);
        Assert.Equal(36.53m, review.Draft.Total);

        var open = await client.GetFromJsonAsync<List<ReceiptImportResponse>>("/api/receipts/imports", Json);
        Assert.Contains(open!, i => i.Id == created.Id);

        var image = await client.GetAsync($"/api/receipts/imports/{created.Id}/image");
        Assert.Equal(FakeReceiptReader.JpegBytes, await image.Content.ReadAsByteArrayAsync());

        var confirm = await client.PutAsJsonAsync($"/api/receipts/imports/{created.Id}/confirm", new
        {
            storeId,
            issuedAt = "2026-11-15T14:30:00",
            productsTotal = 37.53m,
            discountTotal = 1.00m,
            additionalCharges = 0m,
            total = 36.53m,
            items = review.Draft.Items.Select(i => new
            {
                description = i.Description,
                quantity = i.Quantity,
                unit = i.Unit,
                unitPrice = i.UnitPrice,
                discount = i.Discount,
                total = i.Total
            })
        });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var receipt = (await confirm.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!;
        Assert.Equal(ReceiptStatus.Confirmed, receipt.Status);
        Assert.True(receipt.HasImage);

        var processing = await client.GetFromJsonAsync<List<ReceiptImportResponse>>($"/api/receipts/{receipt.Id}/processing", Json);
        Assert.Equal(created.Id, Assert.Single(processing!).Id);
        var receiptImage = await client.GetAsync($"/api/receipts/{receipt.Id}/image");
        Assert.Equal(FakeReceiptReader.JpegBytes, await receiptImage.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Without_a_configured_reader_upload_still_works_and_fails_gracefully()
    {
        var client = CreateClient(new UnavailableReceiptReader("A leitura automática não está configurada."));

        var status = await client.GetFromJsonAsync<ReaderStatusResponse>("/api/receipts/imports/reader", Json);
        Assert.False(status!.Available);
        Assert.Equal("A leitura automática não está configurada.", status.Message);

        var upload = await client.PostAsync("/api/receipts/upload", ImageForm(FakeReceiptReader.JpegBytes));
        var created = (await upload.Content.ReadFromJsonAsync<ReceiptImportResponse>(Json))!;
        var failed = await WaitForAsync(client, created.Id, ReceiptImportStatus.Failed);
        Assert.Equal("A leitura automática não está configurada.", failed.Error);

        var discard = await client.PutAsync($"/api/receipts/imports/{created.Id}/discard", null);
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_files_that_are_not_images()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/receipts/upload", ImageForm("<html></html>"u8.ToArray(), "image/jpeg"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_import_returns_not_found()
    {
        var client = CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/receipts/imports/{Guid.NewGuid()}")).StatusCode);
    }
}
