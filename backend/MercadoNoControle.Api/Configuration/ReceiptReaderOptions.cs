namespace MercadoNoControle.Api.Configuration;

/// <summary>
/// Configuração da leitura automática de notas (seção "ReceiptReader").
/// A chave de API fica exclusivamente no backend: variável de ambiente
/// <c>ANTHROPIC_API_KEY</c> ou <c>ReceiptReader__ApiKey</c>, ou user-secrets em desenvolvimento.
/// Nunca versionar a chave em appsettings.
/// </summary>
public sealed class ReceiptReaderOptions
{
    public const string SectionName = "ReceiptReader";

    /// <summary>"Anthropic" (padrão) ou "None" (desativa a leitura automática).</summary>
    public string Provider { get; set; } = "Anthropic";

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Profundidade de raciocínio do modelo: low, medium, high, xhigh ou max.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Tempo máximo de uma leitura, em segundos.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    public string? ApiKey { get; set; }
}
