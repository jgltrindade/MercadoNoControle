using MercadoNoControle.Api.Services.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Configuration;

/// <summary>
/// Converte exceções em respostas padronizadas no formato ProblemDetails (RFC 9457).
/// Erros inesperados são registrados em log e não expõem detalhes internos.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            NotFoundException e => Create(StatusCodes.Status404NotFound, "Recurso não encontrado", e.Message),
            ConflictException e => CreateConflict(e),
            BusinessRuleException e => CreateBusinessRule(e),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                Create(499, "Requisição cancelada", "A requisição foi cancelada pelo cliente."),
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Erro não tratado ao processar {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = Create(StatusCodes.Status500InternalServerError, "Erro interno", "Ocorreu um erro inesperado. Tente novamente.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Create(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ProblemDetails CreateConflict(ConflictException exception)
    {
        var problem = Create(StatusCodes.Status409Conflict, "Conflito", exception.Message);
        foreach (var (key, value) in exception.Extensions)
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }

    private static ProblemDetails CreateBusinessRule(BusinessRuleException exception)
    {
        var problem = new ValidationProblemDetails(exception.Errors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Regra de negócio violada",
            Detail = exception.Message
        };
        return problem;
    }
}
