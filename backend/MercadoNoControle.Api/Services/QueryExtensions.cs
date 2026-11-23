using System.Linq.Expressions;
using MercadoNoControle.Api.DTOs.Common;
using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Services;

public static class QueryExtensions
{
    /// <summary>Conta o total, aplica a página solicitada e materializa o resultado.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Ordena por um dos campos permitidos. O desempate por <paramref name="tieBreaker"/>
    /// garante paginação estável. Campo desconhecido gera erro de validação.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        PageQuery page,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortFields,
        string defaultField,
        Expression<Func<T, object?>> tieBreaker,
        bool defaultDescending = false)
    {
        var field = string.IsNullOrWhiteSpace(page.SortBy) ? defaultField : page.SortBy;
        var descending = string.IsNullOrWhiteSpace(page.SortDirection) ? defaultDescending : page.Descending;
        if (!sortFields.TryGetValue(field, out var keySelector))
        {
            throw new BusinessRuleException(
                "Campo de ordenação inválido.",
                new Dictionary<string, string[]>
                {
                    ["sortBy"] = [$"Valores aceitos: {string.Join(", ", sortFields.Keys)}."]
                });
        }

        var ordered = descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
        return descending ? ordered.ThenByDescending(tieBreaker) : ordered.ThenBy(tieBreaker);
    }

    public static Dictionary<string, Expression<Func<T, object?>>> SortFields<T>() =>
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Caractere de escape usado com <see cref="ContainsPattern"/> em EF.Functions.Like.</summary>
    public const string LikeEscape = "\\";

    /// <summary>Padrão LIKE "contém", escapando os curingas digitados pelo usuário.</summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
