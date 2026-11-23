using System.Net;
using System.Threading.RateLimiting;
using MercadoNoControle.Api.Models;
using MercadoNoControle.Api.Services.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MercadoNoControle.Api.Configuration;

/// <summary>
/// Autenticação por cookie de sessão, limite de tentativas de login, cabeçalhos de segurança e
/// proxy reverso (especificação, seção 10).
/// </summary>
public static class SecurityConfiguration
{
    public const string SessionCookieName = "mnc.session";
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>Tamanho máximo de requisição fora dos envios de imagem (que têm limite próprio).</summary>
    public const long MaxRequestBodyBytes = 2 * 1024 * 1024;

    public static WebApplicationBuilder AddAppSecurity(this WebApplicationBuilder builder, StoragePaths storagePaths)
    {
        var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        // Chaves que protegem o cookie de sessão: persistidas junto ao banco para que reinícios
        // (ou um contêiner recriado) não encerrem as sessões.
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("MercadoNoControle")
            .PersistKeysToFileSystem(new DirectoryInfo(storagePaths.KeysDirectory));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                // Strict: o navegador não envia o cookie em requisições originadas de outros sites (CSRF).
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(Math.Max(1, authOptions.SessionDays));
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    // API: responde 401/403 em vez de redirecionar para uma página de login.
                    OnRedirectToLogin = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                        "Não autenticado", "Entre com seu e-mail e senha para continuar."),
                    OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                        "Acesso negado", "Você não tem acesso a este recurso."),
                    OnValidatePrincipal = async context =>
                    {
                        var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                        if (context.Principal is null || !await auth.IsSessionValidAsync(context.Principal, context.HttpContext.RequestAborted))
                        {
                            context.RejectPrincipal();
                        }
                    }
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            if (authOptions.Enabled)
            {
                // Toda rota exige sessão, exceto as marcadas com [AllowAnonymous].
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            }
        });

        // Força bruta: poucas tentativas de login/cadastro por endereço IP.
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            options.OnRejected = (context, _) => new ValueTask(WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
                "Muitas tentativas", "Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente."));
        });

        // Atrás de um proxy reverso (HTTPS terminado no proxy), confia nos cabeçalhos X-Forwarded-*.
        if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        return builder;
    }

    /// <summary>Cabeçalhos de segurança em todas as respostas.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/api")
                    ? "default-src 'none'; frame-ancestors 'none'"
                    : SpaContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });
        await next();
    });

    /// <summary>
    /// Política da interface publicada pela própria API: scripts apenas do próprio site; estilos
    /// inline (Angular Material/ECharts) e fontes do Google Fonts.
    /// </summary>
    public const string SpaContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Requisição originada do próprio computador (o cadastro inicial só é aceito assim, por padrão).</summary>
    public static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        return problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        }).AsTask();
    }
}
