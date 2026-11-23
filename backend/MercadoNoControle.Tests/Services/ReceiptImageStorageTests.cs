using MercadoNoControle.Api.Configuration;
using MercadoNoControle.Api.Services.Exceptions;
using MercadoNoControle.Api.Services.Storage;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace MercadoNoControle.Tests.Services;

public class ReceiptImageStorageTests : IDisposable
{
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "mnc-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_uploads))
        {
            Directory.Delete(_uploads, recursive: true);
        }
    }

    private ReceiptImageStorage CreateStorage(long maxBytes = 1024) =>
        new(new StoragePaths("unused.db", _uploads), Options.Create(new ImageUploadOptions { MaxImageSizeBytes = maxBytes }), new FixedTimeProvider());

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 9, 9];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray(), 1, 2];

    [Theory]
    [InlineData("jpeg", ".jpg", "image/jpeg")]
    [InlineData("webp", ".webp", "image/webp")]
    public async Task Stores_supported_images_by_content_signature(string kind, string extension, string contentType)
    {
        var bytes = kind == "jpeg" ? Jpeg : Webp;

        var stored = await CreateStorage().SaveAsync(new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        Assert.Matches($@"^receipts/2026/11/[0-9a-f]{{32}}\{extension}$", stored.RelativePath);
        Assert.Equal(contentType, stored.ContentType);
        Assert.Equal(bytes.Length, stored.Size);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_uploads, stored.RelativePath)));
    }

    [Fact]
    public async Task Rejects_files_that_are_not_images_even_with_image_extension()
    {
        var text = "<script>alert(1)</script>"u8.ToArray();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateStorage().SaveAsync(new MemoryStream(text), text.Length, CancellationToken.None));

        Assert.True(error.Errors.ContainsKey("file"));
        Assert.False(Directory.Exists(_uploads) && Directory.EnumerateFiles(_uploads, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Rejects_empty_and_oversized_files()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateStorage().SaveAsync(new MemoryStream(), 0, CancellationToken.None));

        var big = new byte[2048];
        Jpeg.CopyTo(big, 0);
        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateStorage(maxBytes: 1024).SaveAsync(new MemoryStream(big), big.Length, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_streams_larger_than_the_declared_length()
    {
        var big = new byte[2048];
        Jpeg.CopyTo(big, 0);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateStorage(maxBytes: 1024).SaveAsync(new MemoryStream(big), 100, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_uploads, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Refuses_paths_outside_the_uploads_directory()
    {
        Assert.Throws<InvalidOperationException>(() => CreateStorage().OpenRead("../../secret.txt"));
        Assert.Throws<NotFoundException>(() => CreateStorage().OpenRead("receipts/2026/11/missing.jpg"));
    }
}
