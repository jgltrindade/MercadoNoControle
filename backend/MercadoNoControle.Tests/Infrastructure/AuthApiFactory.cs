using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MercadoNoControle.Tests.Infrastructure;

/// <summary>
/// API com autenticação ativa. O TestServer não informa o IP de origem; o cabeçalho
/// <see cref="RemoteIpHeader"/> simula o endereço do cliente (padrão: loopback).
/// </summary>
public sealed class AuthApiFactory : ApiFactory
{
    public const string RemoteIpHeader = "X-Test-Remote-IP";

    protected override bool AuthenticationEnabled => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var header = context.Request.Headers[RemoteIpHeader].ToString();
                context.Connection.RemoteIpAddress = string.IsNullOrEmpty(header) ? IPAddress.Loopback : IPAddress.Parse(header);
                await nextMiddleware();
            });
            next(app);
        };
    }
}
