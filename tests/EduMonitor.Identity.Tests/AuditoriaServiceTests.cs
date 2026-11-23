using EduMonitor.Identity.Api.Contracts;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Domain;
using EduMonitor.Identity.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduMonitor.Identity.Tests;

/// <summary>T17.x — Bitácora centralizada: filtros, paginación y secretos ocultos (HU-17).</summary>
public class AuditoriaServiceTests
{
    private static IdentityDbContext CrearDb()
    {
        var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hoy = DateTime.UtcNow.Date;
        db.Auditoria.AddRange(
            new RegistroAuditoria { UsuarioEmail = "ana@uta.edu.ec", Accion = "LOGIN_EXITOSO", Servicio = "MS-A", Fecha = hoy.AddDays(-3), Detalle = "ok" },
            new RegistroAuditoria { UsuarioEmail = "ana@uta.edu.ec", Accion = "AGENTE_CONSULTA", Servicio = "MS-C", Fecha = hoy.AddDays(-1).AddHours(10), Detalle = "materia 1" },
            new RegistroAuditoria { UsuarioEmail = "luis@uta.edu.ec", Accion = "AGENTE_ACCESO_DENEGADO", Servicio = "MS-C", Fecha = hoy.AddHours(8), Detalle = "token=abc123secreto" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task ListaLoMasRecientePrimero_ConTotalYPaginas()
    {
        using var db = CrearDb();
        var r = await new AuditoriaService(db).ConsultarAsync(new AuditoriaFiltro(null, null, null, null, null, 1, 2));
        Assert.Equal(3, r.Total);
        Assert.Equal(2, r.Items.Count);
        Assert.Equal("AGENTE_ACCESO_DENEGADO", r.Items[0].Accion);
    }

    [Fact]
    public async Task FiltraPorAccionServicioUsuarioYFechas()
    {
        using var db = CrearDb();
        var s = new AuditoriaService(db);
        Assert.Equal(1, (await s.ConsultarAsync(new AuditoriaFiltro("LOGIN_EXITOSO", null, null, null, null))).Total);
        Assert.Equal(2, (await s.ConsultarAsync(new AuditoriaFiltro(null, "MS-C", null, null, null))).Total);
        Assert.Equal(2, (await s.ConsultarAsync(new AuditoriaFiltro(null, null, "ANA", null, null))).Total);
        var hoy = DateTime.UtcNow.Date;
        Assert.Equal(2, (await s.ConsultarAsync(new AuditoriaFiltro(null, null, null, hoy.AddDays(-1), hoy))).Total);
    }

    [Fact]
    public async Task AlLeer_LosSecretosSiguenOcultos()
    {
        using var db = CrearDb();
        var r = await new AuditoriaService(db).ConsultarAsync(new AuditoriaFiltro("AGENTE_ACCESO_DENEGADO", null, null, null, null));
        Assert.DoesNotContain("abc123secreto", r.Items[0].Detalle);
    }

    [Fact]
    public async Task RegistrarGuardaElEventoSinSecretos()
    {
        using var db = CrearDb();
        var s = new AuditoriaService(db);
        var ok = await s.RegistrarAsync("ana@uta.edu.ec", new RegistrarAuditoriaRequest("reporte_generado", "MS-C", "Authorization: Bearer abcdefghijklmnop"));
        Assert.True(ok);
        var guardado = await db.Auditoria.OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal("REPORTE_GENERADO", guardado.Accion);
        Assert.DoesNotContain("abcdefghijklmnop", guardado.Detalle);
    }

    [Theory]
    [InlineData(null, "MS-C")]
    [InlineData("", "MS-C")]
    [InlineData("ACCION", " ")]
    public async Task RechazaSolicitudesIncompletas(string? accion, string servicio)
    {
        using var db = CrearDb();
        Assert.False(await new AuditoriaService(db).RegistrarAsync("ana@uta.edu.ec", new RegistrarAuditoriaRequest(accion!, servicio, null)));
        Assert.False(await new AuditoriaService(db).RegistrarAsync("ana@uta.edu.ec", null));
    }

    [Fact]
    public async Task ListaLasAccionesDisponiblesSinRepetir()
    {
        using var db = CrearDb();
        var acciones = await new AuditoriaService(db).AccionesAsync();
        Assert.Equal(3, acciones.Count);
        Assert.Equal(acciones.OrderBy(a => a).ToList(), acciones);
    }
}
