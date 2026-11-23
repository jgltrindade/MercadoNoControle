using System.Text.Json;
using System.Text.Json.Nodes;

namespace MercadoNoControle.Api.Services.Ai;

/// <summary>Instruções e esquema JSON (saída estruturada) da leitura de notas fiscais.</summary>
public static class ReceiptExtractionPrompt
{
    public const string System = """
        Você transcreve dados de notas e cupons fiscais brasileiros (NFC-e, NF-e, CF-e SAT, cupom fiscal) a partir de fotografias.

        Regras:
        1. Transcreva somente o que está legível na imagem. Nunca invente, estime, complete ou corrija valores. Se um campo não aparece ou não pode ser lido com segurança, use null.
        2. Números: use ponto como separador decimal e copie o valor exatamente como impresso, sem arredondar e sem fazer contas.
        3. issueDate: data e hora de emissão no formato "AAAA-MM-DDTHH:MM:SS" (horário local impresso, sem fuso). Se a nota mostrar só a data, use "AAAA-MM-DD".
        4. CNPJ e chave de acesso: copie os caracteres na ordem impressa (podem ter letras; pontuação e espaços são opcionais).
        5. Itens: um por linha de produto, na ordem impressa.
           - description: exatamente como impressa, sem expandir abreviações.
           - quantity e unit (ex.: UN, KG, L, CX, PCT): como impressos.
           - unitPrice: valor unitário impresso.
           - totalPrice: valor total impresso na linha do item, antes de qualquer desconto do item.
           - discount: desconto aplicado a esse item quando a nota o mostrar (na linha do item ou logo abaixo dela); 0 quando o item não tiver desconto.
           - Ignore itens marcados como cancelados.
        6. Totais da nota: totalProducts (valor total dos produtos), discount (descontos), additionalCharges (acréscimos/outras despesas) e total (valor a pagar). Use 0 para desconto ou acréscimo somente quando a nota indicar claramente que não houve; null quando não for possível ler.
        7. paymentMethod: forma de pagamento como impressa (ex.: "Cartão de Débito", "Dinheiro", "Pix").
        8. Se a imagem não for uma nota fiscal, ou estiver ilegível a ponto de não permitir a leitura dos itens, responda isReceipt = false, explique o motivo em unreadableReason (em português) e deixe os demais campos nulos ou vazios.
        """;

    public const string Instruction = "Transcreva os dados desta nota fiscal conforme as regras.";

    /// <summary>
    /// Esquema JSON da resposta. Segue as restrições da saída estruturada: todos os objetos com
    /// additionalProperties = false e todas as propriedades listadas em required; campos opcionais
    /// aceitam null via anyOf.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, JsonElement> Schema = BuildSchema();

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        var item = StrictObject(new()
        {
            ["description"] = Nullable("string"),
            ["quantity"] = Nullable("number"),
            ["unit"] = Nullable("string"),
            ["unitPrice"] = Nullable("number"),
            ["discount"] = Nullable("number"),
            ["totalPrice"] = Nullable("number")
        });

        var root = StrictObject(new()
        {
            ["isReceipt"] = new JsonObject { ["type"] = "boolean" },
            ["unreadableReason"] = Nullable("string"),
            ["store"] = StrictObject(new()
            {
                ["name"] = Nullable("string"),
                ["cnpj"] = Nullable("string")
            }),
            ["receipt"] = StrictObject(new()
            {
                ["number"] = Nullable("string"),
                ["series"] = Nullable("string"),
                ["accessKey"] = Nullable("string"),
                ["issueDate"] = Nullable("string"),
                ["totalProducts"] = Nullable("number"),
                ["discount"] = Nullable("number"),
                ["additionalCharges"] = Nullable("number"),
                ["total"] = Nullable("number"),
                ["paymentMethod"] = Nullable("string")
            }),
            ["items"] = new JsonObject { ["type"] = "array", ["items"] = item }
        });

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(root.ToJsonString())!;
    }

    private static JsonObject StrictObject(Dictionary<string, JsonNode> properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Keys.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray()),
            ["additionalProperties"] = false
        };
    }

    private static JsonObject Nullable(string type) => new()
    {
        ["anyOf"] = new JsonArray(new JsonObject { ["type"] = type }, new JsonObject { ["type"] = "null" })
    };
}
