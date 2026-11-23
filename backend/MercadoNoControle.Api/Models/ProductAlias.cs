namespace MercadoNoControle.Api.Models;

/// <summary>
/// Descrição alternativa encontrada em notas para um produto padronizado
/// (especificação: ProdutoDescricao). Ex.: "LEITE INT 1L" → "Leite Integral 1L".
/// </summary>
public class ProductAlias
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Descrição exatamente como aparece na nota.</summary>
    public required string Description { get; set; }

    /// <summary>Descrição normalizada (maiúsculas, sem espaços extras) usada na busca.</summary>
    public required string NormalizedDescription { get; set; }

    /// <summary>Associação confirmada pelo usuário. Sugestões da IA começam como não confirmadas.</summary>
    public bool Confirmed { get; set; }

    public AliasSource Source { get; set; } = AliasSource.Manual;

    public DateTime CreatedAt { get; set; }
}
