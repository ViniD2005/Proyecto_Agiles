using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Academic.Api.Services;
using EduMonitor.Common.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduMonitor.Academic.Tests;

public sealed class FakePublisher : IEventPublisher
{
    public List<(string Evento, object Mensaje)> Publicados { get; } = new();
    public bool Disponible { get; set; } = true;

    public Task<bool> PublishAsync<T>(string routingKey, T message, CancellationToken ct = default)
    {
        if (!Disponible) return Task.FromResult(false);
        Publicados.Add((routingKey, message!));
        return Task.FromResult(true);
    }
}

/// <summary>Moodle simulado: devuelve lo que cada prueba configure, sin red.</summary>
public sealed class FakeMoodle : IMoodleClient
{
    public string BaseUrl => "http://moodle-falso";
    public List<MoodleUser> Usuarios { get; set; } = new();
    public List<MoodleSection> Secciones { get; set; } = new();
    public List<MoodleUserGrades> Notas { get; set; } = new();
    public List<MoodleAssignSubmissions> Entregas { get; set; } = new();
    public bool FallaNotas { get; set; }
    public bool FallaEntregas { get; set; }

    public Task<MoodleSiteInfo> GetSiteInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new MoodleSiteInfo("Moodle falso", "ws", "4.5", "2024", 1));
    public Task<MoodleUser?> GetUserByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<MoodleUser?>(null);
    public Task<IReadOnlyList<MoodleCourse>> GetUserCoursesAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MoodleCourse>>(new List<MoodleCourse>());
    public Task<IReadOnlyList<MoodleUser>> GetEnrolledUsersAsync(int courseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MoodleUser>>(Usuarios);
    public Task<IReadOnlyList<MoodleSection>> GetCourseContentsAsync(int courseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MoodleSection>>(Secciones);

    public Task<IReadOnlyList<MoodleUserGrades>> GetGradeItemsAsync(int courseId, int userId = 0, CancellationToken ct = default)
    {
        if (FallaNotas) throw new MoodleException("accessexception", "Sin permiso para leer calificaciones.");
        IReadOnlyList<MoodleUserGrades> r = userId == 0 ? Notas : Notas.Where(n => n.UserId == userId).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<MoodleAssignSubmissions>> GetAssignSubmissionsAsync(IReadOnlyCollection<int> assignmentIds, CancellationToken ct = default)
    {
        if (FallaEntregas) throw new MoodleException("accessexception", "Sin permiso para leer entregas.");
        return Task.FromResult<IReadOnlyList<MoodleAssignSubmissions>>(Entregas);
    }
}

public static class TestSupport
{
    public static AcademicDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<AcademicDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static MapeoEventDispatcher Dispatcher(AcademicDbContext db, FakePublisher pub) =>
        new(new EventSnapshotBuilder(db), pub, NullLogger<MapeoEventDispatcher>.Instance);

    public static SyncService Sync(AcademicDbContext db, FakeMoodle moodle, FakePublisher pub) =>
        new(db, moodle, pub, new EventSnapshotBuilder(db), NullLogger<SyncService>.Instance);

    public static async Task<Materia> SembrarMateriaAsync(AcademicDbContext db, string docente = "docente1@uta.edu.ec", int moodleCourseId = 2)
    {
        var materia = new Materia { MoodleCourseId = moodleCourseId, Nombre = "Programación Web - Cuarto B", NombreCorto = "PW-401-B", DocenteEmail = docente };
        db.Materias.Add(materia);
        await db.SaveChangesAsync();
        return materia;
    }

    public static MoodleUser Estudiante(int id, string? nombre, string? apellido, string? completo = null) =>
        new(id, nombre, apellido, completo, $"u{id}@uta.edu.ec", new List<MoodleRole> { new(5, "student") });

    public static MoodleSection Seccion(params (int cmid, string nombre, int instancia)[] tareas) =>
        new(1, "Primer Parcial", 1, tareas.Select(t => new MoodleModule(t.cmid, t.nombre, t.instancia, "assign", 1)).ToList());

    public static MoodleGradeItem Item(int cmid, decimal? bruta, decimal max = 10m, string? formateado = null) =>
        new(cmid, "x", "mod", "assign", cmid, cmid, bruta, max, formateado ?? (bruta?.ToString("0.00") ?? "-"));
}
