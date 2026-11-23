using System.Text.Json;
using Anthropic;
using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Services.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Tests.Services;

public class ReceiptExtractionPromptTests
{
    [Fact]
    public void Schema_follows_structured_output_rules()
    {
        var root = JsonSerializer.SerializeToElement(ReceiptExtractionPrompt.Schema);

        AssertStrictObject(root);
        var properties = root.GetProperty("properties");
        Assert.Equal(["isReceipt", "unreadableReason", "store", "receipt", "items"], properties.EnumerateObject().Select(p => p.Name));
        AssertStrictObject(properties.GetProperty("store"));
        AssertStrictObject(properties.GetProperty("receipt"));
        AssertStrictObject(properties.GetProperty("items").GetProperty("items"));
    }

    [Fact]
    public void Schema_uses_the_contract_field_names_of_the_specification()
    {
        var receipt = JsonSerializer.SerializeToElement(ReceiptExtractionPrompt.Schema)
            .GetProperty("properties").GetProperty("receipt").GetProperty("properties");

        Assert.Equal(
            ["number", "series", "accessKey", "issueDate", "totalProducts", "discount", "additionalCharges", "total", "paymentMethod"],
            receipt.EnumerateObject().Select(p => p.Name));
    }

    private static void AssertStrictObject(JsonElement schema)
    {
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var names = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        var required = schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).ToList();
        Assert.Equal(names, required);
    }
}

public class ClaudeReceiptReaderTests
{
    private static ClaudeReceiptReader CreateReader() => new(
        new AnthropicClient { ApiKey = "test-key-never-used" },
        Options.Create(new ReceiptReaderOptions()),
        NullLogger<ClaudeReceiptReader>.Instance);

    [Fact]
    public void Parses_the_structured_response_keeping_exact_decimals()
    {
        const string json = """
            {"isReceipt":true,"unreadableReason":null,
             "store":{"name":"MERCADO","cnpj":"11222333000181"},
             "receipt":{"number":"1","series":null,"accessKey":null,"issueDate":"2026-11-15T14:30:00",
                        "totalProducts":28.90,"discount":0,"additionalCharges":0,"total":28.90,"paymentMethod":null},
             "items":[{"description":"QUEIJO KG","quantity":0.315,"unit":"KG","unitPrice":59.90,"discount":0,"totalPrice":18.87}]}
            """;

        var extraction = ClaudeReceiptReader.ParseResponse("end_turn", json);

        Assert.True(extraction.IsReceipt);
        Assert.Equal(28.90m, extraction.Receipt!.Total);
        Assert.Null(extraction.Receipt.Series);
        var item = Assert.Single(extraction.Items);
        Assert.Equal(0.315m, item.Quantity);
        Assert.Equal(18.87m, item.TotalPrice);
    }

    [Theory]
    [InlineData("refusal", "{}", ReceiptReaderErrorKind.Refused)]
    [InlineData("max_tokens", "{\"isReceipt\":", ReceiptReaderErrorKind.InvalidResponse)]
    [InlineData("end_turn", "", ReceiptReaderErrorKind.InvalidResponse)]
    [InlineData("end_turn", "não é JSON", ReceiptReaderErrorKind.InvalidResponse)]
    [InlineData("end_turn", "null", ReceiptReaderErrorKind.InvalidResponse)]
    public void Rejects_unusable_responses(string stopReason, string text, ReceiptReaderErrorKind expected)
    {
        var error = Assert.Throws<ReceiptReaderException>(() => ClaudeReceiptReader.ParseResponse(stopReason, text));

        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task Rejects_images_above_the_provider_limit_before_calling_the_api()
    {
        var oversized = new byte[ClaudeReceiptReader.MaxImageBytes + 1];
        FakeJpegHeader().CopyTo(oversized, 0);

        var error = await Assert.ThrowsAsync<ReceiptReaderException>(() =>
            CreateReader().ExtractAsync(new MemoryStream(oversized), CancellationToken.None));

        Assert.Equal(ReceiptReaderErrorKind.InvalidImage, error.Kind);
        Assert.Contains("5 MB", error.Message);
    }

    [Fact]
    public async Task Rejects_content_that_is_not_a_supported_image()
    {
        var error = await Assert.ThrowsAsync<ReceiptReaderException>(() =>
            CreateReader().ExtractAsync(new MemoryStream("GIF89a........"u8.ToArray()), CancellationToken.None));

        Assert.Equal(ReceiptReaderErrorKind.InvalidImage, error.Kind);
    }

    private static byte[] FakeJpegHeader() => [0xFF, 0xD8, 0xFF, 0xE0];
}

public class ReceiptReaderFactoryTests
{
    private static IReceiptReader Create(ReceiptReaderOptions options)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Options.Create(options))
            .BuildServiceProvider();
        return ReceiptReaderFactory.Create(services);
    }

    [Fact]
    public void Uses_claude_when_a_key_is_configured()
    {
        var reader = Create(new ReceiptReaderOptions { ApiKey = "configured" });

        Assert.IsType<ClaudeReceiptReader>(reader);
        Assert.True(reader.IsAvailable);
        Assert.Equal("claude-opus-5-5", reader.ModelName);
    }

    [Theory]
    [InlineData("None", "desativada")]
    [InlineData("OutroProvedor", "não suportado")]
    public void Falls_back_to_manual_registration_when_disabled_or_unknown(string provider, string reason)
    {
        var reader = Create(new ReceiptReaderOptions { Provider = provider, ApiKey = "configured" });

        var unavailable = Assert.IsType<UnavailableReceiptReader>(reader);
        Assert.False(unavailable.IsAvailable);
        Assert.Contains(reason, unavailable.Reason);
    }

    [Fact]
    public void Without_any_key_the_reader_is_unavailable()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            return; // Ambiente com chave definida: o cenário "sem chave" não se aplica.
        }

        var reader = Create(new ReceiptReaderOptions());

        Assert.False(reader.IsAvailable);
    }

    [Fact]
    public async Task Unavailable_reader_reports_not_configured()
    {
        var reader = new UnavailableReceiptReader("sem chave");

        var error = await Assert.ThrowsAsync<ReceiptReaderException>(() => reader.ExtractAsync(Stream.Null, CancellationToken.None));

        Assert.Equal(ReceiptReaderErrorKind.NotConfigured, error.Kind);
        Assert.Equal("sem chave", error.Message);
    }
}
