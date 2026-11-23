namespace MercadoNoControle.Api.Services.Exceptions;

/// <summary>Recurso inexistente → HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>
/// Conflito com o estado atual (duplicidade, recurso em uso, alertas não confirmados) → HTTP 409.
/// <see cref="Extensions"/> é incluído na resposta ProblemDetails (ex.: lista de alertas).
/// </summary>
public class ConflictException(string message, IReadOnlyDictionary<string, object?>? extensions = null) : Exception(message)
{
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

/// <summary>Violação de regra de negócio → HTTP 422.</summary>
public class BusinessRuleException(string message, IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

/// <summary>Credenciais inválidas ou sessão ausente → HTTP 401.</summary>
public class UnauthorizedException(string message) : Exception(message);
