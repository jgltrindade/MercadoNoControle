using MercadoNoControle.Api.DTOs.Analytics;
using MercadoNoControle.Api.DTOs.Reports;
using MercadoNoControle.Api.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Controllers;

/// <summary>Indicadores e distribuições do dashboard. Período padrão: últimos 12 meses.</summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<DashboardSummary> Summary([FromQuery] AnalyticsQuery query, CancellationToken cancellationToken) =>
        dashboardService.GetSummaryAsync(query, cancellationToken);

    /// <summary>Total por mês (meses sem compra aparecem com zero).</summary>
    [HttpGet("monthly")]
    public Task<IReadOnlyList<MonthlyTotal>> Monthly([FromQuery] AnalyticsQuery query, CancellationToken cancellationToken) =>
        dashboardService.GetMonthlyAsync(query, cancellationToken);

    [HttpGet("stores")]
    public Task<IReadOnlyList<StoreTotal>> Stores([FromQuery] AnalyticsQuery query, CancellationToken cancellationToken) =>
        dashboardService.GetStoresAsync(query, cancellationToken);

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryTotal>> Categories([FromQuery] AnalyticsQuery query, CancellationToken cancellationToken) =>
        dashboardService.GetCategoriesAsync(query, cancellationToken);

    /// <summary>Produtos com maior participação nas despesas.</summary>
    [HttpGet("products")]
    public Task<IReadOnlyList<ProductTotal>> Products([FromQuery] AnalyticsQuery query, [FromQuery] int top, CancellationToken cancellationToken) =>
        dashboardService.GetProductsAsync(query, top == 0 ? 10 : top, cancellationToken);
}

/// <summary>Relatórios tabulares e exportações (CSV, Excel, PDF) com os mesmos filtros.</summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(IReportService reportService, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("monthly")]
    public Task<ReportTable> Monthly([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("monthly", query, cancellationToken);

    [HttpGet("stores")]
    public Task<ReportTable> Stores([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("stores", query, cancellationToken);

    [HttpGet("categories")]
    public Task<ReportTable> Categories([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("categories", query, cancellationToken);

    /// <summary>Ranking de produtos: <c>ranking</c> = purchases, spent (padrão) ou increase.</summary>
    [HttpGet("products")]
    public Task<ReportTable> Products([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("products", query, cancellationToken);

    [HttpGet("history")]
    public Task<ReportTable> History([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("history", query, cancellationToken);

    /// <summary>Compara o período com <c>compareFrom</c>/<c>compareTo</c> (padrão: período anterior de mesma duração).</summary>
    [HttpGet("comparison")]
    public Task<ReportTable> Comparison([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        reportService.BuildAsync("comparison", query, cancellationToken);

    /// <summary>Exporta o relatório com os filtros informados em <c>csv</c>, <c>xlsx</c> ou <c>pdf</c>.</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export([FromQuery] ExportQuery query, CancellationToken cancellationToken)
    {
        var table = await reportService.BuildAsync(query.Report!, query, cancellationToken);
        var file = ReportExporter.Export(table, query.Format!, DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));
        return File(file.Content, file.ContentType, file.FileName);
    }
}
