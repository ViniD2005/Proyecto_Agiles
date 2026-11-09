using EduMonitor.Common.Persistence;
using EduMonitor.Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Identity.Api.Data;

/// <summary>
/// Aplica las migraciones y siembra usuarios de prueba.
/// Los correos de los docentes deben existir también en el Moodle de prueba (moodle/2_usuarios.csv).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration config, ILogger logger)
    {
        // Sprint 2: migraciones de EF Core (antes EnsureCreated), con reintentos mientras SQL Server inicia
        await DatabaseMigrator.MigrateWithRetryAsync<IdentityDbContext>(sp, logger);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (await db.Usuarios.AnyAsync()) return;

        var pwd = config["Seed:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(pwd))
        {
            logger.LogWarning("Seed:DefaultPassword no configurado; no se siembran usuarios.");
            return;
        }
        var hash = BCrypt.Net.BCrypt.HashPassword(pwd, workFactor: 11);

        db.Usuarios.AddRange(
            new Usuario { Nombre = "Docente Uno (Prog. Web y Base de Datos)", Email = "docente1@uta.edu.ec", PasswordHash = hash, RolId = 1 },
            new Usuario { Nombre = "Docente Dos (sin materias, prueba de permisos)", Email = "docente2@uta.edu.ec", PasswordHash = hash, RolId = 1 },
            new Usuario { Nombre = "Administrador Técnico", Email = "admin@edumonitor.local", PasswordHash = hash, RolId = 2 });
        await db.SaveChangesAsync();
        logger.LogInformation("Usuarios de prueba sembrados.");
    }
}
