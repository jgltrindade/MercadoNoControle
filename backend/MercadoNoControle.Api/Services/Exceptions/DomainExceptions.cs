namespace MercadoNoControle.Api.Services.Exceptions;

/// <summary>Recurso inexistente → HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Conflito com o estado atual (duplicidade, recurso em uso) → HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>Violação de regra de negócio → HTTP 422.</summary>
public class BusinessRuleException(string message, IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
