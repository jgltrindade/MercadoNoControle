namespace MercadoNoControle.Api.DTOs;

/// <summary>Estado da API e de suas dependências locais.</summary>
public sealed record SystemStatusResponse(
    string Status,
    bool DatabaseAvailable,
    string Version,
    string Environment,
    DateTime ServerTimeUtc);
