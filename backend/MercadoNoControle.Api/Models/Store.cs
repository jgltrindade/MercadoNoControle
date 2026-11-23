namespace MercadoNoControle.Api.Models;

/// <summary>Supermercado / estabelecimento (especificação: Supermercado).</summary>
public class Store
{
    public int Id { get; set; }

    public required string TradeName { get; set; }

    public string? LegalName { get; set; }

    /// <summary>
    /// CNPJ normalizado, sem máscara (14 caracteres; numérico ou alfanumérico).
    /// Único quando informado.
    /// </summary>
    public string? Cnpj { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    /// <summary>UF com duas letras.</summary>
    public string? State { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Receipt> Receipts { get; set; } = [];
}
