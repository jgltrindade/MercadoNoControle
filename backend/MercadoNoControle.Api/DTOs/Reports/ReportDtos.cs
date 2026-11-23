using System.ComponentModel.DataAnnotations;
using MercadoNoControle.Api.DTOs.Analytics;

namespace MercadoNoControle.Api.DTOs.Reports;

public enum ReportColumnType
{
    Text,
    Integer,
    Number,
    Money,
    Percent,
    Date
}

public sealed record ReportColumn(string Key, string Header, ReportColumnType Type);

/// <summary>Linha do relatório. <see cref="IsTotal"/> marca subtotais e totais.</summary>
public sealed record ReportRow(IReadOnlyList<object?> Cells, bool IsTotal = false);

/// <summary>
/// Relatório tabular. A mesma tabela é exibida na tela e usada nas exportações (CSV, Excel, PDF),
/// garantindo que o arquivo reflita exatamente os filtros e os dados consultados.
/// </summary>
public sealed record ReportTable(
    string Key,
    string Title,
    string Filters,
    string? Notes,
    DateTime GeneratedAt,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<ReportRow> Rows);

public class ReportQuery : AnalyticsQuery
{
    /// <summary>Relatório de produtos: purchases (mais comprados), spent (maior gasto) ou increase (maior aumento).</summary>
    [RegularExpression("^(purchases|spent|increase)$", ErrorMessage = "Use purchases, spent ou increase.")]
    public string? Ranking { get; set; }

    [Range(1, 200)]
    public int Top { get; set; } = 50;

    /// <summary>Período de comparação (padrão: período de mesma duração imediatamente anterior).</summary>
    public DateOnly? CompareFrom { get; set; }

    public DateOnly? CompareTo { get; set; }
}

public sealed class ExportQuery : ReportQuery
{
    [Required(ErrorMessage = "Informe o relatório.")]
    [RegularExpression("^(monthly|stores|categories|products|history|comparison)$", ErrorMessage = "Relatório inválido.")]
    public string? Report { get; set; }

    [Required(ErrorMessage = "Informe o formato.")]
    [RegularExpression("^(csv|xlsx|pdf)$", ErrorMessage = "Use csv, xlsx ou pdf.")]
    public string? Format { get; set; }
}
