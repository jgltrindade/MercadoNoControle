using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.DTOs.Receipts;
using MercadoNoControle.Api.DTOs.Reports;
using MercadoNoControle.Api.DTOs.Stores;
using MercadoNoControle.Tests.Infrastructure;

namespace MercadoNoControle.Tests.Api;

/// <summary>Consistência entre compra confirmada, dashboard, relatórios e exportações via HTTP.</summary>
public class AnalyticsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Dashboard_reports_and_exports_reflect_the_same_purchases()
    {
        var storeResponse = await _client.PostAsJsonAsync("/api/stores", new { tradeName = "Loja Dashboard HTTP" });
        var store = (await storeResponse.Content.ReadFromJsonAsync<StoreResponse>(Json))!;
        var created = await _client.PostAsJsonAsync("/api/receipts", new
        {
            storeId = store.Id,
            issuedAt = "2024-03-15T10:00:00",
            productsTotal = 42.50m,
            total = 42.50m,
            items = new[] { new { description = "ITEM DASHBOARD", quantity = 1m, unitPrice = 42.50m, total = 42.50m } }
        });
        var receipt = (await created.Content.ReadFromJsonAsync<ReceiptResponse>(Json))!;
        (await _client.PutAsJsonAsync($"/api/receipts/{receipt.Id}/confirm", new { acknowledgeWarnings = true })).EnsureSuccessStatusCode();

        const string filters = "from=2024-03-01&to=2024-03-31";
        var summary = await _client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard?{filters}&storeId={store.Id}", Json);
        Assert.Equal(42.50m, summary!.TotalSpent);
        Assert.Equal(1, summary.PurchaseCount);

        var monthly = await _client.GetFromJsonAsync<List<MonthlyTotal>>($"/api/dashboard/monthly?{filters}&storeId={store.Id}", Json);
        Assert.Equal(42.50m, Assert.Single(monthly!).Total);

        var report = await _client.GetFromJsonAsync<ReportTable>($"/api/reports/history?{filters}&storeId={store.Id}", Json);
        Assert.Equal("Histórico de compras", report!.Title);
        Assert.Equal(2, report.Rows.Count);

        var csv = await _client.GetAsync($"/api/reports/export?report=history&format=csv&{filters}&storeId={store.Id}");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.StartsWith("mercado-no-controle-history-", csv.Content.Headers.ContentDisposition!.FileNameStar ?? csv.Content.Headers.ContentDisposition.FileName);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("15/03/2024;Loja Dashboard HTTP;;1;42,50", text);

        var pdf = await _client.GetAsync($"/api/reports/export?report=stores&format=pdf&{filters}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var xlsx = await _client.GetAsync($"/api/reports/export?report=categories&format=xlsx&{filters}");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/export?report=unknown&format=csv")]
    [InlineData("/api/reports/export?report=stores&format=doc")]
    [InlineData("/api/reports/products?ranking=other")]
    public async Task Invalid_report_requests_return_bad_request(string url)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Invalid_period_returns_unprocessable_entity()
    {
        var response = await _client.GetAsync("/api/dashboard?from=2026-12-01&to=2026-11-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
