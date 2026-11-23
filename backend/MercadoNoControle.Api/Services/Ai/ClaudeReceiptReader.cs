using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Services.Storage;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Api.Services.Ai;

/// <summary>
/// Leitura de notas com a API da Anthropic (Claude), usando imagem em base64 e saída
/// estruturada (JSON validado pelo esquema de <see cref="ReceiptExtractionPrompt"/>).
/// </summary>
/// <remarks>
/// Recusas por política de segurança são re-atendidas por um modelo de reserva no mesmo pedido
/// (fallback do lado do servidor, beta <c>server-side-fallback-2026-07-01</c>).
/// </remarks>
public sealed class ClaudeReceiptReader(
    AnthropicClient client,
    IOptions<ReceiptReaderOptions> options,
    ILogger<ClaudeReceiptReader> logger) : IReceiptReader
{
    /// <summary>Limite de tamanho de imagem aceito pela API para envio em base64.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    private const int MaxOutputTokens = 16000;

    public string ProviderName => "Anthropic";

    public string? ModelName => options.Value.Model;

    public bool IsAvailable => true;

    public async Task<ReceiptExtractionResult> ExtractAsync(Stream image, CancellationToken cancellationToken)
    {
        var bytes = await ReadImageAsync(image, cancellationToken);
        var mediaType = ImageSignature.DetectContentType(bytes)
                        ?? throw new ReceiptReaderException(ReceiptReaderErrorKind.InvalidImage, "Formato de imagem não suportado para leitura automática.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(BuildRequest(bytes, mediaType), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReceiptReaderException(ReceiptReaderErrorKind.Timeout, "A leitura demorou mais que o permitido. Tente novamente.");
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw Fail(ReceiptReaderErrorKind.NotConfigured, "A chave da API de leitura foi recusada. Verifique a configuração.", ex);
        }
        catch (AnthropicForbiddenException ex)
        {
            throw Fail(ReceiptReaderErrorKind.NotConfigured, "A conta da API de leitura não tem permissão para este modelo.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw Fail(ReceiptReaderErrorKind.Unavailable, "Limite de uso da API de leitura atingido. Tente novamente em instantes.", ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            throw Fail(ReceiptReaderErrorKind.InvalidImage, "A API de leitura não aceitou a imagem. Tente outra foto ou cadastre manualmente.", ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw Fail(ReceiptReaderErrorKind.Unavailable, "O serviço de leitura está indisponível no momento. Tente novamente mais tarde.", ex);
        }
        catch (AnthropicIOException ex)
        {
            throw Fail(ReceiptReaderErrorKind.Unavailable, "Não foi possível conectar ao serviço de leitura.", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw Fail(ReceiptReaderErrorKind.Unavailable, "O serviço de leitura retornou um erro. Tente novamente.", ex);
        }

        var text = string.Concat(response.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : null)
            .Where(t => t is not null));

        var extraction = ParseResponse(response.StopReason?.Raw(), text);
        return new ReceiptExtractionResult(extraction, text, ProviderName, response.Model.Raw());
    }

    /// <summary>Valida a resposta do modelo e a converte no contrato de extração.</summary>
    public static ReceiptExtraction ParseResponse(string? stopReason, string text)
    {
        switch (stopReason)
        {
            case "refusal":
                throw new ReceiptReaderException(ReceiptReaderErrorKind.Refused, "O serviço de leitura recusou esta imagem. Cadastre a compra manualmente.");
            case "max_tokens":
                throw new ReceiptReaderException(ReceiptReaderErrorKind.InvalidResponse, "A leitura foi interrompida antes do fim (nota muito longa). Cadastre manualmente ou tente uma foto por partes.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ReceiptReaderException(ReceiptReaderErrorKind.InvalidResponse, "O serviço de leitura não retornou dados.");
        }

        try
        {
            return JsonSerializer.Deserialize<ReceiptExtraction>(text, JsonSerializerOptions.Web)
                   ?? throw new JsonException("Resposta nula.");
        }
        catch (JsonException ex)
        {
            throw new ReceiptReaderException(ReceiptReaderErrorKind.InvalidResponse, "O serviço de leitura retornou dados em formato inválido.", ex);
        }
    }

    private MessageCreateParams BuildRequest(byte[] image, string mediaType) => new()
    {
        Model = options.Value.Model,
        MaxTokens = MaxOutputTokens,
        Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
        Fallbacks = new Default(),
        System = ReceiptExtractionPrompt.System,
        OutputConfig = new BetaOutputConfig
        {
            Effort = ParseEffort(options.Value.Effort),
            Format = new BetaJsonOutputFormat { Schema = ReceiptExtractionPrompt.Schema }
        },
        Messages =
        [
            new BetaUserMessageParam
            {
                Content = new List<BetaContentBlockParam>
                {
                    new BetaImageBlockParam
                    {
                        Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(image), MediaType = mediaType }
                    },
                    new BetaTextBlockParam { Text = ReceiptExtractionPrompt.Instruction }
                }
            }
        ]
    };

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium
    };

    private static async Task<byte[]> ReadImageAsync(Stream image, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImageBytes)
        {
            throw new ReceiptReaderException(
                ReceiptReaderErrorKind.InvalidImage,
                "A imagem excede 5 MB, o limite da leitura automática. Envie uma foto com resolução menor ou cadastre manualmente.");
        }

        return buffer.ToArray();
    }

    private ReceiptReaderException Fail(ReceiptReaderErrorKind kind, string message, Exception exception)
    {
        logger.LogWarning(exception, "Falha na leitura de nota pela API da Anthropic ({Kind}).", kind);
        return new ReceiptReaderException(kind, message, exception);
    }
}

/// <summary>Leitor usado quando a leitura automática não está configurada: sempre informa a indisponibilidade.</summary>
public sealed class UnavailableReceiptReader(string reason) : IReceiptReader
{
    public string ProviderName => "Nenhum";

    public string? ModelName => null;

    public bool IsAvailable => false;

    public string Reason { get; } = reason;

    public Task<ReceiptExtractionResult> ExtractAsync(Stream image, CancellationToken cancellationToken) =>
        throw new ReceiptReaderException(ReceiptReaderErrorKind.NotConfigured, Reason);
}

/// <summary>Escolhe o leitor conforme a configuração.</summary>
public static class ReceiptReaderFactory
{
    public static IReceiptReader Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<ReceiptReaderOptions>>();
        var settings = options.Value;

        if (string.Equals(settings.Provider, "None", StringComparison.OrdinalIgnoreCase))
        {
            return new UnavailableReceiptReader("A leitura automática está desativada. Cadastre a compra manualmente.");
        }

        if (!string.Equals(settings.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return new UnavailableReceiptReader($"Provedor de leitura \"{settings.Provider}\" não suportado.");
        }

        var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey)
            ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new UnavailableReceiptReader(
                "A leitura automática não está configurada (chave de API ausente). Cadastre a compra manualmente.");
        }

        var client = new AnthropicClient { ApiKey = apiKey, MaxRetries = 2 };
        return new ClaudeReceiptReader(client, options, services.GetRequiredService<ILogger<ClaudeReceiptReader>>());
    }
}
