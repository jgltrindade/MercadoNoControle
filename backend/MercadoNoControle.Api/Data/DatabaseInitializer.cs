using MercadoNoControle.Api.Services.Receipts;
using Microsoft.EntityFrameworkCore;

namespace MercadoNoControle.Api.Data;

/// <summary>
/// Cria/atualiza o banco SQLite aplicando as migrations pendentes na inicialização e mantém os
/// dados derivados coerentes com as regras atuais (preço normalizado do histórico).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Aplicando {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        }

        await db.Database.MigrateAsync(cancellationToken);

        // Observações registradas antes do cálculo de preço normalizado (ou com regra anterior)
        // são atualizadas. A operação é idempotente.
        var recalculated = await scope.ServiceProvider.GetRequiredService<PriceObservationRecorder>()
            .RecalculateAsync(null, cancellationToken);
        if (recalculated > 0)
        {
            logger.LogInformation("Preço normalizado recalculado em {Count} observação(ões) de preço.", recalculated);
        }

        // Senha esquecida: com Authentication:ResetUser=true o usuário é removido (dados preservados)
        // e o próximo acesso pede o cadastro inicial. A opção deve ser retirada em seguida.
        if (app.Configuration.GetValue<bool>("Authentication:ResetUser"))
        {
            var removed = await Services.Auth.AuthService.ResetUsersAsync(db, cancellationToken);
            logger.LogWarning(
                "Authentication:ResetUser ativo: {Count} usuário(s) removido(s). Faça o cadastro inicial e remova a opção da configuração.",
                removed);
        }
    }
}
