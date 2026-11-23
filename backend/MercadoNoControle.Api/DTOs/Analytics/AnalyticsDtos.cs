namespace MercadoNoControle.Api.DTOs.Analytics;

/// <summary>Filtros comuns do dashboard e dos relatórios (somente compras confirmadas — RN09).</summary>
public class AnalyticsQuery
{
    /// <summary>Data inicial (inclusive) da compra.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Data final (inclusive) da compra.</summary>
    public DateOnly? To { get; set; }

    public int? StoreId { get; set; }

    /// <summary>
    /// Com categoria, os valores passam a ser a soma dos itens dessa categoria (e não o total das notas)
    /// e só contam as compras que têm ao menos um item da categoria.
    /// </summary>
    public int? CategoryId { get; set; }
}

public sealed record PurchaseReference(int ReceiptId, decimal Total, DateTime Date, string StoreName);

public sealed record StoreReference(int StoreId, string StoreName, decimal Total);

public sealed record DashboardSummary(
    DateOnly From,
    DateOnly To,
    decimal TotalSpent,
    int PurchaseCount,
    decimal? AverageTicket,
    PurchaseReference? LargestPurchase,
    StoreReference? TopStore,
    /// <summary>Total no mês corrente (mesmos filtros de supermercado e categoria).</summary>
    decimal CurrentMonthTotal,
    /// <summary>Total no ano corrente (mesmos filtros de supermercado e categoria).</summary>
    decimal CurrentYearTotal);

public sealed record MonthlyTotal(int Year, int Month, decimal Total, int PurchaseCount);

public sealed record StoreTotal(int StoreId, string StoreName, decimal Total, int PurchaseCount, decimal SharePercent);

/// <summary>Gasto por categoria. <see cref="CategoryId"/> nulo agrupa itens sem categoria ou ajustes da nota.</summary>
public sealed record CategoryTotal(int? CategoryId, string CategoryName, decimal Total, decimal SharePercent);

public sealed record ProductTotal(int ProductId, string ProductName, string? CategoryName, decimal Total, int PurchaseCount, decimal SharePercent);
