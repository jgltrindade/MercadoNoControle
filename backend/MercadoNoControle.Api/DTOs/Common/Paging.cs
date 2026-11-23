using System.ComponentModel.DataAnnotations;

namespace MercadoNoControle.Api.DTOs.Common;

/// <summary>Parâmetros comuns de listagens paginadas (query string).</summary>
public class PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Página, iniciando em 1.</summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Texto livre de busca.</summary>
    [MaxLength(100)]
    public string? Search { get; set; }

    /// <summary>Campo de ordenação (valores aceitos variam por recurso).</summary>
    [MaxLength(30)]
    public string? SortBy { get; set; }

    /// <summary>"asc" ou "desc".</summary>
    [RegularExpression("^(?i)(asc|desc)$", ErrorMessage = "Use 'asc' ou 'desc'.")]
    public string? SortDirection { get; set; }

    public bool Descending => string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
