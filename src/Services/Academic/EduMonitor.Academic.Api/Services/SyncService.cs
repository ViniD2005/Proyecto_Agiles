using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Moodle;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Services;

public interface ISyncService
{
    Task<ResultadoSincronizacion> SincronizarMateriasAsync(string emailDocente, CancellationToken ct = default);
    Task<ResultadoSincronizacion> SincronizarDetalleAsync(Materia materia, string emailDocente, CancellationToken ct = default);
}

/// <summary>
/// Sincronización manual desde Moodle (HU-05 y HU-06). Solo lee de Moodle (RN-06).
/// Si Moodle falla se registra el error y se conservan los últimos datos (HU-05, criterio 2).
/// </summary>
public class SyncService(AcademicDbContext db, IMoodleClient moodle, ILogger<SyncService> logger) : ISyncService
{
    public async Task<ResultadoSincronizacion> SincronizarMateriasAsync(string emailDocente, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        try
        {
            var docente = await moodle.GetUserByEmailAsync(emailDocente, ct)
                ?? throw new MoodleException("docente_no_encontrado", $"No existe en Moodle un usuario con el correo {emailDocente}.");

            var cursos = await moodle.GetUserCoursesAsync(docente.Id, ct);
            if (cursos.Count == 0)
    throw new MoodleException("sin_cursos",
        $"Moodle no devolvió materias para {emailDocente}. Verifica que esté matriculado como profesor en sus cursos y que el token tenga permiso para verlos.");
            foreach (var c in cursos)
            {
                var materia = await db.Materias.FirstOrDefaultAsync(m => m.MoodleCourseId == c.Id, ct);
                if (materia is null)
                {
                    materia = new Materia { MoodleCourseId = c.Id };
                    db.Materias.Add(materia);
                }
                materia.Nombre = c.FullName;
                materia.NombreCorto = c.ShortName;
                materia.DocenteEmail = emailDocente;
                materia.DocenteMoodleUserId = docente.Id;
                materia.UltimaSincronizacion = ahora;
                materia.OrigenSincronizacion = moodle.BaseUrl;
            }
            await db.SaveChangesAsync(ct);

            // Detalle de cada materia (estudiantes y actividades) para mostrar los contadores de M2
            var materias = await db.Materias.Where(m => m.DocenteEmail == emailDocente).ToListAsync(ct);
            var errores = new List<string>();
foreach (var m in materias.Where(m => cursos.Any(c => c.Id == m.MoodleCourseId)))
{
    var r = await SincronizarDetalleAsync(m, emailDocente, ct);
    if (!r.Exitosa)
        errores.Add($"{m.NombreCorto}: {r.Mensaje}");
    else if (m.TotalActividades == 0)
        errores.Add($"{m.NombreCorto}: Moodle no devolvió actividades calificables (tareas o cuestionarios).");
}

if (errores.Count > 0)
    return await RegistrarAsync(null, "MATERIAS", false, string.Join(" | ", errores), emailDocente, ahora, cursos.Count, ct);

return await RegistrarAsync(null, "MATERIAS", true, $"{cursos.Count} materia(s) sincronizada(s).", emailDocente, ahora, cursos.Count, ct);
        }
        catch (Exception ex) when (ex is MoodleException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fallo al sincronizar materias de {Email}", emailDocente);
            return await RegistrarAsync(null, "MATERIAS", false, MensajeError(ex), emailDocente, ahora, 0, ct);
        }
    }

    public async Task<ResultadoSincronizacion> SincronizarDetalleAsync(Materia materia, string emailDocente, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        if (db.Entry(materia).State == EntityState.Detached) db.Materias.Attach(materia);
        try
        {
            // ---- Estudiantes (core_enrol_get_enrolled_users) ----
            var usuarios = await moodle.GetEnrolledUsersAsync(materia.MoodleCourseId, ct);
            var alumnos = usuarios.Where(u => u.Roles?.Any(r => r.ShortName == "student") == true).ToList();

            var actuales = await db.Matriculas.Where(x => x.MateriaId == materia.Id).ToListAsync(ct);
            foreach (var mat in actuales) mat.Activa = false;

            foreach (var u in alumnos)
            {
                var est = await db.Estudiantes.FirstOrDefaultAsync(e => e.MoodleUserId == u.Id, ct);
                if (est is null)
                {
                    est = new Estudiante { MoodleUserId = u.Id };
                    db.Estudiantes.Add(est);
                }
                est.Nombres = u.FirstName ?? string.Empty;
                est.Apellidos = u.LastName ?? string.Empty;
                est.Email = u.Email;
                await db.SaveChangesAsync(ct);

                var matricula = actuales.FirstOrDefault(x => x.EstudianteId == est.Id);
                if (matricula is null)
                    db.Matriculas.Add(new Matricula { MateriaId = materia.Id, EstudianteId = est.Id, Activa = true });
                else
                    matricula.Activa = true;
            }

            // ---- Actividades (core_course_get_contents) ----
            var secciones = await moodle.GetCourseContentsAsync(materia.MoodleCourseId, ct);
            var existentes = await db.Actividades.Where(a => a.MateriaId == materia.Id).ToListAsync(ct);
            var vigentes = new List<Actividad>();

            foreach (var s in secciones)
            foreach (var mod in s.Modules ?? new())
            {
                if (!ActivityClassifier.ModulosCalificables.Contains(mod.ModName)) continue;
                var act = existentes.FirstOrDefault(a => a.MoodleCmId == mod.Id);
                if (act is null)
                {
                    act = new Actividad { MateriaId = materia.Id, MoodleCmId = mod.Id };
                    db.Actividades.Add(act);
                }
                act.MoodleInstanceId = mod.Instance;
                act.Modulo = mod.ModName;
                act.Nombre = mod.Name;
                act.Seccion = s.Name;
                act.Tipo = ActivityClassifier.Clasificar(mod.Name);
                vigentes.Add(act);
            }
            ActivityClassifier.AsignarPesos(vigentes);

            // Actividades que ya no existen en Moodle se eliminan de la copia local
            db.Actividades.RemoveRange(existentes.Where(e => vigentes.All(v => v.MoodleCmId != e.MoodleCmId)));

            materia.TotalEstudiantes = alumnos.Count;
            materia.TotalActividades = vigentes.Count;
            materia.UltimaSincronizacion = ahora;
            materia.OrigenSincronizacion = moodle.BaseUrl;
            await db.SaveChangesAsync(ct);

            return await RegistrarAsync(materia.Id, "DETALLE_MATERIA", true,
                $"{alumnos.Count} estudiante(s) y {vigentes.Count} actividad(es).", emailDocente, ahora, alumnos.Count + vigentes.Count, ct);
        }
        catch (Exception ex) when (ex is MoodleException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fallo al sincronizar la materia {Id}", materia.Id);
            db.ChangeTracker.Clear(); // se descartan cambios parciales: se conservan los últimos datos
            return await RegistrarAsync(materia.Id, "DETALLE_MATERIA", false, MensajeError(ex), emailDocente, ahora, 0, ct);
        }
    }

    private async Task<ResultadoSincronizacion> RegistrarAsync(int? materiaId, string tipo, bool ok, string mensaje,
        string email, DateTime fecha, int elementos, CancellationToken ct)
    {
        db.Sincronizaciones.Add(new SincronizacionLog
        {
            MateriaId = materiaId, Tipo = tipo, Exitosa = ok, Mensaje = mensaje,
            UsuarioEmail = email, Fecha = fecha, Origen = moodle.BaseUrl
        });
        await db.SaveChangesAsync(ct);
        return new ResultadoSincronizacion(ok, mensaje, fecha, moodle.BaseUrl, elementos);
    }

    private static string MensajeError(Exception ex) => ex switch
    {
        MoodleException me => $"Moodle respondió con error ({me.ErrorCode}): {me.Message}",
        TaskCanceledException => "Moodle no respondió a tiempo (timeout).",
        _ => $"No se pudo conectar con Moodle: {ex.Message}"
    };
}
