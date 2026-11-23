using System.Reflection;
using MercadoNoControle.Api.Data;
using MercadoNoControle.Api.DTOs;

namespace MercadoNoControle.Api.Services;

public interface ISystemStatusService
{
    Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class SystemStatusService(
    AppDbContext db,
    IHostEnvironment environment,
    TimeProvider timeProvider) : ISystemStatusService
{
    private static readonly string Version =
        typeof(SystemStatusService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public async Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var databaseAvailable = await db.Database.CanConnectAsync(cancellationToken);

        return new SystemStatusResponse(
            databaseAvailable ? "Healthy" : "Degraded",
            databaseAvailable,
            Version,
            environment.EnvironmentName,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
