using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.DTOs.Imports;
using MercadoNoControle.Api.DTOs.PriceHistory;
using MercadoNoControle.Api.DTOs.Products;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Ai;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MercadoNoControle.Tests.Api;

/// <summary>
/// Roteiro de integração ponta a ponta da especificação (seção 11), com autenticação ativa e o
/// leitor de notas simulado: enviar imagem → processar → conferir → confirmar → persistência →
/// dashboard → histórico do produto → editar e excluir → consistência dos relatórios.
/// </summary>
public class EndToEndTests
{
    private const string Period = "from=2026-11-01&to=2026-11-30";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Complete_purchase_lifecycle_keeps_dashboard_history_and_reports_consistent()
    {
        using var factory = new AuthApiFactory();
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IReceiptReader>(new FakeReceiptReader())))
            .CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/setup", new { name = "Pessoa", email = "eu@example.com", password = "senha-forte-123" })).StatusCode);

        var store = await PostAsync<StoreResponse>(client, "/api/stores", new { tradeName = "Mercado Teste", cnpj = "11222333000181" });
        var banana = await PostAsync<ProductResponse>(client, "/api/products", new { name = "Banana Prata", unit = "KG", categoryId = 2 });
        var rice = await PostAsync<ProductResponse>(client, "/api/products", new { name = "Arroz Tipo 1 5kg", unit = "KG", packageContent = 5m, categoryId = 1 });

        // 1. Enviar imagem.
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakeReceiptReader.JpegBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "nota.jpg");
        var upload = await client.PostAsync("/api/receipts/upload", form);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var importId = (await upload.Content.ReadFromJsonAsync<ReceiptImportResponse>(Json))!.Id;

        // 2. Processar nota (em segundo plano).
        var review = await WaitForReviewAsync(client, importId);

        // 3. Conferir: supermercado reconhecido pelo CNPJ; o usuário associa os itens aos produtos.
        Assert.Equal(store.Id, review.Draft!.StoreId);
        Assert.Equal(36.53m, review.Draft.Total);
        var items = review.Draft.Items.Select((item, index) => new
        {
            description = item.Description,
            productId = (int?)(index == 0 ? banana.Id : rice.Id),
            quantity = item.Quantity,
            unit = item.Unit,
            unitPrice = item.UnitPrice,
            discount = item.Discount,
            total = item.Total
        }).ToList();

        // 4. Confirmar compra.
        var confirm = await client.PutAsJsonAsync($"/api/receipts/imports/{importId}/confirm", new
        {
            storeId = store.Id,
            issuedAt = "2026-11-15T14:30:00",
            productsTotal = 37.53m,
            discountTotal = 1.00m,
            additionalCharges = 0m,
            total = 36.53m,
            items
        });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var receiptId = (await confirm.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!.Id;

        // 5. Verificar persistência.
        var saved = (await client.GetFromJsonAsync<ReceiptResponse>($"/api/receipts/{receiptId}", Json))!;
        Assert.Equal(ReceiptStatus.Confirmed, saved.Status);
        Assert.Equal(36.53m, saved.Total);
        Assert.True(saved.HasImage);
        Assert.Equal([banana.Id, rice.Id], saved.Items.Select(i => i.ProductId!.Value).ToArray());

        // 6. Consultar dashboard.
        var dashboard = (await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard?{Period}", Json))!;
        Assert.Equal(36.53m, dashboard.TotalSpent);
        Assert.Equal(1, dashboard.PurchaseCount);
        Assert.Equal("Mercado Teste", dashboard.TopStore!.StoreName);

        // 7. Consultar histórico do produto: preço pago 27,90 (28,90 − 1,00) e R$ 5,58/kg.
        var history = (await client.GetFromJsonAsync<ProductPriceHistoryResponse>($"/api/products/{rice.Id}/prices", Json))!;
        var point = Assert.Single(history.Points);
        Assert.Equal(27.90m, point.EffectiveUnitPrice);
        Assert.Equal(5.58m, point.NormalizedPrice);

        // 8a. Editar: o arroz passa a custar 30,90 (com o mesmo desconto).
        var update = await client.PutAsJsonAsync($"/api/receipts/{receiptId}", new
        {
            storeId = store.Id,
            issuedAt = "2026-11-15T14:30:00",
            productsTotal = 39.53m,
            discountTotal = 1.00m,
            additionalCharges = 0m,
            total = 38.53m,
            items = saved.Items.Select(i => new
            {
                id = i.Id,
                description = i.Description,
                productId = i.ProductId,
                quantity = i.Quantity,
                unit = i.Unit,
                unitPrice = i.ProductId == rice.Id ? 30.90m : i.UnitPrice,
                discount = i.Discount,
                total = i.ProductId == rice.Id ? 29.90m : i.Total
            })
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // 9a. Consistência após a edição: dashboard, histórico e relatórios refletem o novo valor.
        Assert.Equal(38.53m, (await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard?{Period}", Json))!.TotalSpent);
        var edited = (await client.GetFromJsonAsync<ProductPriceHistoryResponse>($"/api/products/{rice.Id}/prices", Json))!;
        Assert.Equal(29.90m, Assert.Single(edited.Points).EffectiveUnitPrice);
        Assert.Equal(38.53m, await ReportTotalAsync(client, "monthly"));
        Assert.Equal(38.53m, await ReportTotalAsync(client, "stores"));
        Assert.Equal(38.53m, await ReportTotalAsync(client, "categories"));

        // 8b. Excluir: compra, itens e histórico de preços somem; relatórios zeram.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/receipts/{receiptId}")).StatusCode);
        Assert.Equal(0m, (await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard?{Period}", Json))!.TotalSpent);
        Assert.Empty((await client.GetFromJsonAsync<ProductPriceHistoryResponse>($"/api/products/{rice.Id}/prices", Json))!.Points);
        Assert.Equal(0m, await ReportTotalAsync(client, "monthly"));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<ReceiptImportResponse> WaitForReviewAsync(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var import = (await client.GetFromJsonAsync<ReceiptImportResponse>($"/api/receipts/imports/{id}", Json))!;
            if (import.Status == ReceiptImportStatus.AwaitingReview)
            {
                return import;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("A leitura não terminou.");
    }

    /// <summary>Valor da coluna de total na última linha de total do relatório (ou 0 sem linhas).</summary>
    private static async Task<decimal> ReportTotalAsync(HttpClient client, string report)
    {
        var table = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{report}?{Period}");
        var columns = table.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToList();
        var totalIndex = columns.IndexOf("total");
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        if (report == "monthly")
        {
            var totals = rows.Where(r => r.GetProperty("isTotal").GetBoolean()).ToList();
            return totals.Count == 0 ? 0m : totals[^1].GetProperty("cells")[totalIndex].GetDecimal();
        }

        return rows.Where(r => !r.GetProperty("isTotal").GetBoolean()).Sum(r => r.GetProperty("cells")[totalIndex].GetDecimal());
    }
}
