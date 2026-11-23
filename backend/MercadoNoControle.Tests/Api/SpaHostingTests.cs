using System.Net;
using MercadoNoControle.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace MercadoNoControle.Tests.Api;

/// <summary>Publicação: a API serve a interface compilada (Spa:RootPath) na mesma origem.</summary>
public sealed class SpaHostingTests : IDisposable
{
    private readonly DirectoryInfo _spa = Directory.CreateTempSubdirectory("mnc-spa");
    private readonly SpaFactory _factory;

    public SpaHostingTests()
    {
        File.WriteAllText(Path.Combine(_spa.FullName, "index.html"), "<!doctype html><app-root></app-root>");
        File.WriteAllText(Path.Combine(_spa.FullName, "main-ABC123.js"), "console.log('app');");
        _factory = new SpaFactory(_spa.FullName);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _spa.Delete(recursive: true);
    }

    [Fact]
    public async Task Serves_the_interface_files_with_cache_rules_and_csp()
    {
        var client = _factory.CreateClient();

        var index = await client.GetAsync("/");
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());
        Assert.Contains("script-src 'self'", index.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        var script = await client.GetAsync("/main-ABC123.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", script.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Angular_routes_return_the_index_but_unknown_api_routes_stay_404()
    {
        var client = _factory.CreateClient();

        var deepLink = await client.GetAsync("/receipts/10");
        Assert.Equal(HttpStatusCode.OK, deepLink.StatusCode);
        Assert.Contains("<app-root>", await deepLink.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var api = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
    }

    private sealed class SpaFactory(string spaRoot) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Spa:RootPath", spaRoot);
        }
    }
}
