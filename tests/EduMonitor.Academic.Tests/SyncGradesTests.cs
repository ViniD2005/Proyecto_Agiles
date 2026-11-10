using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Common.Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EduMonitor.Academic.Tests.TestSupport;

namespace EduMonitor.Academic.Tests;

/// <summary>HU-07 · T7.1, T7.2, T7.4 y T7.6 — sincronización de calificaciones con un Moodle simulado.</summary>
public class SyncGradesTests
{
    // Ana (id 10) y Joel (id 11) en un curso con 3 tareas: Evaluación 1, APE 1 y Trabajo autónomo 1
    private static FakeMoodle MoodleBase()
    {
        var m = new FakeMoodle
        {
            Usuarios = { Estudiante(10, "Ana", "Pérez"), Estudiante(11, "Joel", "Herrera") },
            Secciones = { Seccion((3, "Evaluación 1", 1), (4, "APE 1", 2), (5, "Trabajo autónomo 1", 3)) },
            Notas =
            {
                // Ana: 9 · 8 · sin nota y sin entrega
                new MoodleUserGrades(10, new() { Item(3, 9m), Item(4, 8m), Item(5, null) }),
                // Joel: 2,5 · sin nota pero ENTREGADA · nota cero explícita
                new MoodleUserGrades(11, new() { Item(3, 2.5m), Item(4, null), Item(5, 0m) }),
            },
            Entregas = { new MoodleAssignSubmissions(2, new() { new MoodleSubmission(1, 11, "submitted") }) }
        };
        return m;
    }

    [Fact]
    public async Task LasNotasDelSistemaCoincidenConLasDeMoodle_YSeDistinguenLosTresEstados()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var pub = new FakePublisher();

        var r = await Sync(db, MoodleBase(), pub).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        Assert.True(r.Exitosa);
        var notas = await db.Calificaciones.Include(c => c.Actividad).Include(c => c.Estudiante).ToListAsync();
        Assert.Equal(6, notas.Count);

        EstadoCalificacion Estado(int moodleUser, string actividad) =>
            notas.Single(n => n.Estudiante!.MoodleUserId == moodleUser && n.Actividad!.Nombre == actividad).Estado;
        decimal? Nota(int moodleUser, string actividad) =>
            notas.Single(n => n.Estudiante!.MoodleUserId == moodleUser && n.Actividad!.Nombre == actividad).Nota;

        Assert.Equal(9m, Nota(10, "Evaluación 1"));
        Assert.Equal(EstadoCalificacion.Calificada, Estado(10, "Evaluación 1"));
        Assert.Equal(EstadoCalificacion.SinEntrega, Estado(10, "Trabajo autónomo 1"));          // sin nota y sin entrega
        Assert.Equal(EstadoCalificacion.PendienteDeCalificar, Estado(11, "APE 1"));            // sin nota pero entregada
        Assert.Equal(EstadoCalificacion.Cero, Estado(11, "Trabajo autónomo 1"));               // 0 explícito
        Assert.Null(Nota(11, "APE 1"));
        Assert.Equal(0m, Nota(11, "Trabajo autónomo 1"));
    }

    [Fact]
    public async Task PublicaCalificacionesSincronizadas_ConLaFotoCompleta_SinCorreos()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var pub = new FakePublisher();

        await Sync(db, MoodleBase(), pub).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        var (evento, mensaje) = Assert.Single(pub.Publicados);
        Assert.Equal(EventNames.CalificacionesSincronizadas, evento);
        var e = (CalificacionesSincronizadas)mensaje;
        Assert.Equal(2, e.Estudiantes.Count);
        Assert.Equal(3, e.Actividades.Count);
        Assert.Equal(6, e.Calificaciones.Count);
        Assert.Equal("docente1@uta.edu.ec", e.Materia.DocenteEmail);
        Assert.Equal(40m, e.Actividades.Single(a => a.Nombre == "Evaluación 1").Peso);
        Assert.Contains(e.Calificaciones, c => c.Estado == EstadosCalificacion.PendienteDeCalificar);
    }

    [Fact]
    public async Task EscalaDistintaDeDiez_SeNormalizaA0_10()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = new FakeMoodle
        {
            Usuarios = { Estudiante(10, "Ana", "Pérez") },
            Secciones = { Seccion((3, "Evaluación 1", 1)) },
            Notas = { new MoodleUserGrades(10, new() { Item(3, 85m, max: 100m) }) }
        };
        await Sync(db, moodle, new FakePublisher()).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");
        Assert.Equal(8.5m, (await db.Calificaciones.SingleAsync()).Nota);
    }

    [Fact]
    public async Task SiGraderawViajaVacio_SeUsaLaNotaFormateada()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = new FakeMoodle
        {
            Usuarios = { Estudiante(10, "Ana", "Pérez") },
            Secciones = { Seccion((3, "Evaluación 1", 1)) },
            Notas = { new MoodleUserGrades(10, new() { Item(3, null, formateado: "7,50") }) }
        };
        await Sync(db, moodle, new FakePublisher()).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");
        var fila = await db.Calificaciones.SingleAsync();
        Assert.Equal(7.5m, fila.Nota);
        Assert.Equal(EstadoCalificacion.Calificada, fila.Estado);
    }

    [Fact]
    public async Task SiMoodleNoDevuelveNombreYApellido_UsaElNombreCompleto()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = new FakeMoodle
        {
            Usuarios = { Estudiante(10, null, null, "Ana Pérez") },
            Secciones = { Seccion((3, "Evaluación 1", 1)) },
            Notas = { new MoodleUserGrades(10, new() { Item(3, 9m) }) }
        };
        await Sync(db, moodle, new FakePublisher()).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");
        Assert.Equal("Ana Pérez", (await db.Estudiantes.SingleAsync()).Nombres);
    }

    [Fact]
    public async Task ResincronizarConservaLasCalificacionesSinDuplicarlas_YElMapeo()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = MoodleBase();
        var pub = new FakePublisher();
        var sync = Sync(db, moodle, pub);

        await sync.SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        // El docente mapea una actividad con un contenido
        var unidad = new Unidad { MateriaId = materia.Id, Nombre = "U1", Orden = 1 };
        var tema = new Tema { Nombre = "T1", Orden = 1 };
        var contenido = new ContenidoMinimo { Nombre = "C1", Orden = 1 };
        tema.Contenidos.Add(contenido); unidad.Temas.Add(tema); db.Unidades.Add(unidad);
        await db.SaveChangesAsync();
        var actividad = await db.Actividades.FirstAsync(a => a.Nombre == "APE 1");
        db.Mapeos.Add(new ActividadContenido { ActividadId = actividad.Id, ContenidoId = contenido.Id });
        await db.SaveChangesAsync();

        // Moodle cambia una nota y se vuelve a sincronizar (HU-09, criterio 4)
        moodle.Notas[0] = new MoodleUserGrades(10, new() { Item(3, 9.5m), Item(4, 8m), Item(5, null) });
        await sync.SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        Assert.Equal(6, await db.Calificaciones.CountAsync());
        Assert.Equal(1, await db.Mapeos.CountAsync());
        Assert.Equal(9.5m, (await db.Calificaciones.Include(c => c.Actividad).Include(c => c.Estudiante)
            .SingleAsync(c => c.Estudiante!.MoodleUserId == 10 && c.Actividad!.Nombre == "Evaluación 1")).Nota);
    }

    [Fact]
    public async Task SiFallanLasNotas_SeInformaElError_YSeConservanEstudiantesYActividades()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = MoodleBase();
        moodle.FallaNotas = true;
        var pub = new FakePublisher();

        var r = await Sync(db, moodle, pub).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        Assert.False(r.Exitosa);
        Assert.Contains("calificaciones", r.Mensaje);
        Assert.Equal(2, await db.Matriculas.CountAsync());
        Assert.Equal(3, await db.Actividades.CountAsync());
        Assert.Equal(0, await db.Calificaciones.CountAsync());
        Assert.Empty(pub.Publicados);   // no se avisa al análisis con datos incompletos
    }

    [Fact]
    public async Task SiNoSePuedenLeerLasEntregas_LasNotasVaciasQuedanComoSinEntrega_ConAviso()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var moodle = MoodleBase();
        moodle.FallaEntregas = true;

        var r = await Sync(db, moodle, new FakePublisher()).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        Assert.True(r.Exitosa);
        Assert.Contains("entregas", r.Mensaje);
        Assert.Equal(0, await db.Calificaciones.CountAsync(c => c.Estado == EstadoCalificacion.PendienteDeCalificar));
    }

    [Fact]
    public async Task SiRabbitMqNoEstaDisponible_LaSincronizacionTerminaConAviso()
    {
        await using var db = NuevaDb();
        var materia = await SembrarMateriaAsync(db);
        var pub = new FakePublisher { Disponible = false };

        var r = await Sync(db, MoodleBase(), pub).SincronizarDetalleAsync(materia, "docente1@uta.edu.ec");

        Assert.True(r.Exitosa);
        Assert.Contains("RabbitMQ", r.Mensaje);
        Assert.Equal(6, await db.Calificaciones.CountAsync());
    }
}
