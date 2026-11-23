namespace MercadoNoControle.Api.Models;

/// <summary>Nota fiscal / compra (especificação: NotaFiscal).</summary>
public class Receipt
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public string? Number { get; set; }

    public string? Series { get; set; }

    /// <summary>Chave de acesso NFC-e/NF-e (44 dígitos), quando disponível. Única quando informada.</summary>
    public string? AccessKey { get; set; }

    /// <summary>Data e horário de emissão, no horário local impresso na nota.</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>Valor dos produtos (escala <see cref="DecimalPrecision.Money"/>).</summary>
    public decimal ProductsTotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal AdditionalCharges { get; set; }

    public decimal Total { get; set; }

    public string? PaymentMethod { get; set; }

    /// <summary>Caminho relativo da imagem original dentro do diretório de uploads.</summary>
    public string? ImagePath { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public ICollection<ReceiptItem> Items { get; set; } = [];

    public ICollection<ReceiptImport> Imports { get; set; } = [];
}
