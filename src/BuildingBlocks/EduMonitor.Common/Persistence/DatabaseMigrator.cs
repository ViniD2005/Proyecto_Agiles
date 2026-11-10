using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduMonitor.Common.Persistence;

public class MigrationsMissingException(string contexto)
    : InvalidOperationException($"{contexto} no tiene migraciones. Ejecute «dotnet ef migrations add Inicial» (ver docs/SPRINT2.md, paso 4).");

/// <summary>
/// Aplica las migraciones de EF Core al arrancar, con reintentos mientras SQL Server termina de iniciar.
/// Reemplaza a EnsureCreated() del Sprint 1 (acción de mejora 2 de la retrospectiva).
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateWithRetryAsync<TContext>(
        IServiceProvider services, ILogger logger, int maxAttempts = 12, CancellationToken ct = default)
        where TContext : DbContext
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                if (!db.Database.GetMigrations().Any())
                    throw new MigrationsMissingException(typeof(TContext).Name);
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Base de datos de {Contexto} actualizada", typeof(TContext).Name);
                return;
            }
            catch (MigrationsMissingException) { throw; }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("BD no disponible (intento {Intento}/{Max}): {Mensaje}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
