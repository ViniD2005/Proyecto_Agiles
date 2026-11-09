using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Messaging;
using EduMonitor.Common.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>
/// T10.3 / T11.4 / T12.4 — Consumo del evento CalificacionesSincronizadas, recálculo automático,
/// umbrales configurables, acceso por docente y mapa de calor. Usa SQLite en memoria.
/// </summary>
public sealed class IngestaYConsultasTests : IDisposable
{
    private const string Docente = "docente1@uta.edu.ec";
    private readonly SqliteConnection _conexion = new("DataSource=:memory:");
    private readonly AnalyticsDbContext _db;
    private readonly RecalculoService _recalculo;
    private readonly SnapshotIngestor _ingestor;
    private readonly AnaliticaService _servicio;

    public IngestaYConsultasTests()
    {
        _conexion.Open();
        _db = new AnalyticsDbContext(new DbContextOptionsBuilder<AnalyticsDbContext>().UseSqlite(_conexion).Options);
        _db.Database.EnsureCreated();
        _recalculo = new RecalculoService(_db, NullLogger<RecalculoService>.Instance);
        _ingestor = new SnapshotIngestor(_db, _recalculo, NullLogger<SnapshotIngestor>.Instance);
        _servicio = new AnaliticaService(_db, _recalculo);
    }

    public void Dispose() { _db.Dispose(); _conexion.Dispose(); }

    // Curso de 3 actividades: Evaluación 1 (40 %), APE 1 (30 %) y Trabajo autónomo 1 (30 %)
    private static readonly List<ActividadSnapshot> Actividades = new()
    {
        new(1, "Evaluación 1", "Evaluacion", 40m, 1),
        new(2, "APE 1", "APE", 30m, 2),
        new(3, "Trabajo autónomo 1", "TrabajoAutonomo", 30m, 3),
    };

    private static CalificacionesSincronizadas Evento(DateTime? cuando = null, decimal notaCarlos = 4.6m) => new(
        Guid.NewGuid(), cuando ?? DateTime.UtcNow, new MateriaSnapshot(1, "Programación Web", Docente),
        new List<EstudianteSnapshot> { new(10, "Lucía", "Ramírez"), new(11, "Carlos", "Mena"), new(12, "Joel", "Herrera") },
        Actividades,
        new List<CalificacionSnapshot>
        {
            // Lucía: 9 · 9 · 9 → aprobada
            new(1, 10, 9m, EstadosCalificacion.Calificada), new(2, 10, 9m, EstadosCalificacion.Calificada), new(3, 10, 9m, EstadosCalificacion.Calificada),
            // Carlos: toda la nota igual a notaCarlos (4,6 por defecto → crítico con 5 y 7)
            new(1, 11, notaCarlos, EstadosCalificacion.Calificada), new(2, 11, notaCarlos, EstadosCalificacion.Calificada), new(3, 11, notaCarlos, EstadosCalificacion.Calificada),
            // Joel: 2,5 · pendiente de calificar · sin entrega
            new(1, 12, 2.5m, EstadosCalificacion.Calificada), new(2, 12, null, EstadosCalificacion.PendienteDeCalificar), new(3, 12, null, EstadosCalificacion.SinEntrega),
        });

    private static MapeoActividadActualizado Mapeo(DateTime? cuando = null) => new(
        Guid.NewGuid(), cuando ?? DateTime.UtcNow, new MateriaSnapshot(1, "Programación Web", Docente),
        new List<UnidadSnapshot> { new(1, "Unidad 1", 1) },
        new List<TemaSnapshot> { new(1, 1, "HTML y CSS", 1), new(2, 1, "JavaScript", 2) },
        new List<ContenidoSnapshot> { new(1, 1, "Etiquetas semánticas", 1), new(2, 2, "Variables y tipos", 1) },
        new List<MapeoSnapshot> { new(1, 1), new(2, 1), new(3, 2) });   // E1 y APE 1 → HTML · TA 1 → JavaScript

    // ---------------------------------------------------------------- ingesta (HU-10 criterio 4)
    [Fact]
    public async Task AlLlegarElEvento_SeCalculanIndicadoresYAlertasSinAccionManual()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());

        var r = await _servicio.ObtenerIndicadoresAsync(1, Docente);
        Assert.True(r.EsOk);
        var ind = r.Valor!;
        Assert.Equal(3, ind.Curso.Estudiantes);
        Assert.Equal("Critico", ind.Estudiantes.Single(e => e.Nombre == "Carlos Mena").Nivel);
        Assert.Equal("Aprobado", ind.Estudiantes.Single(e => e.Nombre == "Lucía Ramírez").Nivel);
        Assert.Equal(9m, ind.Estudiantes.Single(e => e.Nombre == "Lucía Ramírez").NotaPonderada);
        Assert.Equal("Critico", ind.Estudiantes[0].Nivel);   // los críticos aparecen primero
        Assert.Equal("0–10", ind.Escala);

        // Joel: solo la evaluación (40 %) está evaluada → peso evaluado 40 %, nota 2,5
        var joel = ind.Estudiantes.Single(e => e.Nombre == "Joel Herrera");
        Assert.Equal(40m, joel.PesoEvaluado);
        Assert.Equal(2.5m, joel.NotaPonderada);
        Assert.Equal(1.0m, joel.NotaAcumulada);   // 2,5 × 40 / 100
    }

    [Fact]
    public async Task ElEventoEsIdempotente_YUnoMasAntiguoSeIgnora()
    {
        var ahora = DateTime.UtcNow;
        await _ingestor.IngerirCalificacionesAsync(Evento(ahora));
        await _ingestor.IngerirCalificacionesAsync(Evento(ahora.AddSeconds(1)));   // repetido: mismo resultado
        Assert.Equal(9, await _db.Calificaciones.CountAsync());
        Assert.Equal(3, await _db.Indicadores.CountAsync());

        // Un evento viejo con otras notas no pisa a los nuevos
        await _ingestor.IngerirCalificacionesAsync(Evento(ahora.AddMinutes(-5), notaCarlos: 9m));
        var carlos = await _db.Calificaciones.Where(c => c.EstudianteId == 11).Select(c => c.Nota).FirstAsync();
        Assert.Equal(4.6m, carlos);
    }

    [Fact]
    public async Task UnEventoNuevoReemplazaLasNotasAnteriores_YRecalcula()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento(DateTime.UtcNow.AddMinutes(-1)));
        await _ingestor.IngerirCalificacionesAsync(Evento(DateTime.UtcNow, notaCarlos: 8m));

        var carlos = (await _servicio.ObtenerRiesgoAsync(1, Docente)).Valor!.Estudiantes.Single(e => e.Nombre == "Carlos Mena");
        Assert.Equal("Aprobado", carlos.Nivel);
        Assert.Empty(carlos.Alertas);
    }

    // ---------------------------------------------------------------- umbrales configurables (HU-11 criterio 2)
    [Fact]
    public async Task CambiarUnUmbral_RecalculaLaClasificacion()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        Assert.Equal("Critico", (await _servicio.ObtenerRiesgoAsync(1, Docente)).Valor!.Estudiantes.Single(e => e.Nombre == "Carlos Mena").Nivel);

        var r = await _servicio.ActualizarUmbralesAsync(new ActualizarUmbralesRequest(1, 4m, 7m), Docente);

        Assert.True(r.EsOk);
        Assert.True(r.Valor!.Umbrales.Personalizado);
        Assert.Equal(4m, r.Valor.Umbrales.Critico);
        var carlos = r.Valor.Estudiantes.Single(e => e.Nombre == "Carlos Mena");
        Assert.Equal("EnUmbral", carlos.Nivel);   // 4,6 ya no es crítico con umbral 4
        Assert.Empty(carlos.Alertas);             // y tampoco genera alerta de nota baja
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(7, 7)]
    [InlineData(8, 7)]
    [InlineData(5, 11)]
    public async Task UmbralesInvalidos_SeRechazan(int critico, int aprobado)
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        var r = await _servicio.ActualizarUmbralesAsync(new ActualizarUmbralesRequest(1, (decimal)critico, (decimal)aprobado), Docente);
        Assert.Equal(CodigoResultado.Invalido, r.Codigo);
    }

    // ---------------------------------------------------------------- alertas con evidencia (HU-11 criterio 3)
    [Fact]
    public async Task LasAlertasMuestranEvidencia_YElAvisoDeQueSonOrientativas()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        var riesgo = (await _servicio.ObtenerRiesgoAsync(1, Docente)).Valor!;

        Assert.Contains("orientativos", riesgo.Aviso);
        var joel = riesgo.Estudiantes.Single(e => e.Nombre == "Joel Herrera");
        var baja = joel.Alertas.Single(a => a.Tipo == "NotaBaja");
        Assert.Equal("Nota 2,5 en Evaluación 1 (umbral crítico < 5)", baja.Evidencia);
        Assert.Contains("Trabajo autónomo 1", joel.Alertas.Single(a => a.Tipo == "SinEntrega").Evidencia);
    }

    // ---------------------------------------------------------------- acceso por docente (RN-07)
    [Fact]
    public async Task UnDocenteNoVeLasMateriasDeOtro()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        Assert.Equal(CodigoResultado.Prohibido, (await _servicio.ObtenerIndicadoresAsync(1, "docente2@uta.edu.ec")).Codigo);
        Assert.Equal(CodigoResultado.Prohibido, (await _servicio.ObtenerRiesgoAsync(1, null)).Codigo);
        Assert.Equal(CodigoResultado.Prohibido, (await _servicio.ObtenerMapaCalorAsync(1, "docente2@uta.edu.ec")).Codigo);
        Assert.Equal(CodigoResultado.Prohibido, (await _servicio.ObtenerFichaAsync(1, 10, "docente2@uta.edu.ec")).Codigo);
        Assert.Equal(CodigoResultado.Prohibido,
            (await _servicio.ActualizarUmbralesAsync(new ActualizarUmbralesRequest(1, 5m, 7m), "docente2@uta.edu.ec")).Codigo);
        Assert.Equal(CodigoResultado.NoEncontrado, (await _servicio.ObtenerIndicadoresAsync(99, Docente)).Codigo);
    }

    // ---------------------------------------------------------------- indicadores por actividad (HU-10)
    [Fact]
    public async Task LosIndicadoresPorActividad_DistinguenCeroSinEntregaYPendiente()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        var ind = (await _servicio.ObtenerIndicadoresAsync(1, Docente)).Valor!;

        var ape = ind.Actividades.Single(a => a.Nombre == "APE 1");
        Assert.Equal(2, ape.Calificadas);   // Lucía y Carlos
        Assert.Equal(1, ape.Pendientes);    // Joel entregó y falta calificar
        Assert.Equal(0, ape.SinEntrega);

        var ta = ind.Actividades.Single(a => a.Nombre == "Trabajo autónomo 1");
        Assert.Equal(1, ta.SinEntrega);     // Joel
        Assert.Equal(6.8m, ta.Promedio);    // (9 + 4,6) / 2
        Assert.Equal(6, ta.Distribucion.Count);
    }

    // ---------------------------------------------------------------- mapa de calor y ficha (HU-12)
    [Fact]
    public async Task ElMapaDeCalor_CalculaElDesempenoPorTema_YMarcaSinDatos()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        await _ingestor.IngerirMapeoAsync(Mapeo());

        var mapa = (await _servicio.ObtenerMapaCalorAsync(1, Docente)).Valor!;
        Assert.Equal(2, mapa.Temas.Count);
        Assert.Equal(0, mapa.ActividadesSinMapear);

        var html = mapa.Temas.Single(t => t.Tema == "HTML y CSS").TemaId;
        var js = mapa.Temas.Single(t => t.Tema == "JavaScript").TemaId;

        // Joel: HTML = E1 (2,5; 40 %) y APE 1 pendiente → solo cuenta E1 = 2,5 · JavaScript = TA 1 sin entrega → sin datos
        var joel = mapa.Estudiantes.Single(f => f.Nombre == "Joel Herrera");
        Assert.Equal(2.5m, joel.Temas[html].Nota);
        Assert.Equal("Critico", joel.Temas[html].Nivel);
        Assert.Null(joel.Temas[js].Nota);
        Assert.Equal("SinDatos", joel.Temas[js].Nivel);

        // Lucía: todo 9 → logrado
        Assert.Equal("Aprobado", mapa.Estudiantes.Single(f => f.Nombre == "Lucía Ramírez").Temas[html].Nivel);
        Assert.NotNull(mapa.PromedioCursoTemas[html].Nota);
    }

    [Fact]
    public async Task ElMapaDeCalor_FuncionaSinEstructura_ConTodasLasActividadesSinMapear()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        var mapa = (await _servicio.ObtenerMapaCalorAsync(1, Docente)).Valor!;
        Assert.Empty(mapa.Temas);
        Assert.Equal(3, mapa.ActividadesSinMapear);
    }

    [Fact]
    public async Task LaFicha_MuestraNotasConEstado_TemasRelacionadosYAlertas()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        await _ingestor.IngerirMapeoAsync(Mapeo());

        var ficha = (await _servicio.ObtenerFichaAsync(1, 12, Docente)).Valor!;
        Assert.Equal("Joel Herrera", ficha.Nombre);
        Assert.Equal("Critico", ficha.Nivel);

        // HU-12, criterio 2: la nota baja de Evaluación 1 trae los temas y contenidos que evalúa
        var e1 = ficha.Actividades.Single(a => a.Nombre == "Evaluación 1");
        Assert.Equal(2.5m, e1.Nota);
        var rel = Assert.Single(e1.Contenidos);
        Assert.Equal("HTML y CSS", rel.Tema);
        Assert.Equal("Etiquetas semánticas", rel.Contenido);

        // El estado de cada actividad se distingue
        Assert.Equal("PendienteDeCalificar", ficha.Actividades.Single(a => a.Nombre == "APE 1").Estado);
        Assert.Equal("SinEntrega", ficha.Actividades.Single(a => a.Nombre == "Trabajo autónomo 1").Estado);
        Assert.Single(ficha.Evolucion);                 // solo cuenta lo evaluado
        Assert.NotEmpty(ficha.Alertas);
        Assert.Contains("orientativos", ficha.Aviso);
    }

    [Fact]
    public async Task LaFicha_DeUnEstudianteQueNoEsDeLaMateria_NoSeEncuentra()
    {
        await _ingestor.IngerirCalificacionesAsync(Evento());
        Assert.Equal(CodigoResultado.NoEncontrado, (await _servicio.ObtenerFichaAsync(1, 999, Docente)).Codigo);
    }
}
