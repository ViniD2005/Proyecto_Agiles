using EduMonitor.Academic.Api.Contracts;
using EduMonitor.Academic.Api.Data;
using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Moodle;
using EduMonitor.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Services;

public interface ISyncService
{
    Task<ResultadoSincronizacion> SincronizarMateriasAsync(string emailDocente, CancellationToken ct = default);
    Task<ResultadoSincronizacion> SincronizarDetalleAsync(Materia materia, string emailDocente, CancellationToken ct = default);
}

/// <summary>
/// Sincronización manual desde Moodle. Solo lee de Moodle (RN-06).
/// Sprint 1: materias, estudiantes y actividades. Sprint 2 (HU-07): calificaciones y evento a MS-C.
/// Si Moodle falla se registra el error y se conservan los últimos datos (HU-05, criterio 2).
/// </summary>
public class SyncService(
    AcademicDbContext db,
    IMoodleClient moodle,
    IEventPublisher events,
    EventSnapshotBuilder snapshots,
    ILogger<SyncService> logger) : ISyncService
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

        // ---- Paso 1: estudiantes y actividades (Sprint 1) ----
        int totalEstudiantes, totalActividades;
        try
        {
            (totalEstudiantes, totalActividades) = await SincronizarEstudiantesYActividadesAsync(materia, ahora, ct);
        }
        catch (Exception ex) when (ex is MoodleException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fallo al sincronizar la materia {Id}", materia.Id);
            db.ChangeTracker.Clear(); // se descartan cambios parciales: se conservan los últimos datos
            return await RegistrarAsync(materia.Id, "DETALLE_MATERIA", false, MensajeError(ex), emailDocente, ahora, 0, ct);
        }

        // ---- Paso 2: calificaciones (HU-07) ----
        int totalCalificaciones;
        bool entregasDisponibles;
        try
        {
            (totalCalificaciones, entregasDisponibles) = await SincronizarCalificacionesAsync(materia, ahora, ct);
        }
        catch (Exception ex) when (ex is MoodleException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fallo al sincronizar las calificaciones de la materia {Id}", materia.Id);
            db.ChangeTracker.Clear();
            return await RegistrarAsync(materia.Id, "DETALLE_MATERIA", false,
                $"Se sincronizaron {totalEstudiantes} estudiante(s) y {totalActividades} actividad(es), pero no se pudieron obtener las calificaciones: {MensajeError(ex)}",
                emailDocente, ahora, totalEstudiantes + totalActividades, ct);
        }

        // ---- Paso 3: aviso a MS-C por RabbitMQ (no bloquea si el bus no está disponible) ----
        var avisos = new List<string>();
        if (!entregasDisponibles)
            avisos.Add("no se pudo consultar el estado de las entregas: las actividades sin nota se muestran como «sin entrega»");
        try
        {
            var evento = await snapshots.ConstruirCalificacionesAsync(materia, ct);
            if (!await events.PublishAsync(EventNames.CalificacionesSincronizadas, evento, ct))
                avisos.Add("el evento hacia el análisis no se pudo publicar (RabbitMQ no disponible)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo armar el evento de calificaciones de la materia {Id}", materia.Id);
            avisos.Add("no se pudo preparar el evento hacia el análisis");
        }

        var mensaje = $"{totalEstudiantes} estudiante(s), {totalActividades} actividad(es) y {totalCalificaciones} calificación(es).";
        if (avisos.Count > 0) mensaje += " Aviso: " + string.Join("; ", avisos) + ".";

        return await RegistrarAsync(materia.Id, "DETALLE_MATERIA", true, mensaje, emailDocente, ahora,
            totalEstudiantes + totalActividades + totalCalificaciones, ct);
    }

    private async Task<(int Estudiantes, int Actividades)> SincronizarEstudiantesYActividadesAsync(Materia materia, DateTime ahora, CancellationToken ct)
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
            // Corrección Sprint 2: si Moodle no entrega nombre y apellido por separado se usa el nombre completo
            est.Nombres = !string.IsNullOrWhiteSpace(u.FirstName) ? u.FirstName! : (u.FullName ?? string.Empty);
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

        // El mapeo actividad ↔ contenido se conserva: las actividades existentes se actualizan sin cambiar su Id (HU-09, criterio 4)
        db.Actividades.RemoveRange(existentes.Where(e => vigentes.All(v => v.MoodleCmId != e.MoodleCmId)));

        materia.TotalEstudiantes = alumnos.Count;
        materia.TotalActividades = vigentes.Count;
        materia.UltimaSincronizacion = ahora;
        materia.OrigenSincronizacion = moodle.BaseUrl;
        await db.SaveChangesAsync(ct);
        return (alumnos.Count, vigentes.Count);
    }

    /// <summary>HU-07: copia las notas de Moodle distinguiendo nota cero, sin entrega y pendiente de calificar (RN-08).</summary>
    private async Task<(int Total, bool EntregasDisponibles)> SincronizarCalificacionesAsync(Materia materia, DateTime ahora, CancellationToken ct)
    {
        var actividades = await db.Actividades.Where(a => a.MateriaId == materia.Id).ToListAsync(ct);
        var estudiantes = await db.Matriculas.Where(m => m.MateriaId == materia.Id && m.Activa)
            .Select(m => m.Estudiante!).ToListAsync(ct);

        // Calificaciones: una sola llamada para todo el curso y, si algún estudiante falta, una llamada individual
        var porUsuario = new Dictionary<int, List<MoodleGradeItem>>();
        foreach (var g in await moodle.GetGradeItemsAsync(materia.MoodleCourseId, 0, ct))
            porUsuario[g.UserId] = g.GradeItems ?? new List<MoodleGradeItem>();
        foreach (var est in estudiantes.Where(e => !porUsuario.ContainsKey(e.MoodleUserId)))
        {
            var individual = await moodle.GetGradeItemsAsync(materia.MoodleCourseId, est.MoodleUserId, ct);
            porUsuario[est.MoodleUserId] = individual.FirstOrDefault(g => g.UserId == est.MoodleUserId)?.GradeItems ?? new List<MoodleGradeItem>();
        }

        // Entregas: permiten saber si una actividad sin nota está «pendiente de calificar» (entregada) o «sin entrega»
        var entregadas = new HashSet<(int Tarea, int Usuario)>();
        var entregasDisponibles = true;
        var tareas = actividades.Where(a => a.Modulo == "assign").Select(a => a.MoodleInstanceId).Distinct().ToList();
        try
        {
            foreach (var a in await moodle.GetAssignSubmissionsAsync(tareas, ct))
                foreach (var s in a.Submissions ?? new List<MoodleSubmission>())
                    if (string.Equals(s.Status, "submitted", StringComparison.OrdinalIgnoreCase))
                        entregadas.Add((a.AssignmentId, s.UserId));
        }
        catch (MoodleException ex)
        {
            entregasDisponibles = false;
            logger.LogWarning(ex, "No se pudo consultar las entregas de la materia {Id}; se asume «sin entrega» para las notas vacías", materia.Id);
        }

        var actividadIds = actividades.Select(a => a.Id).ToList();
        var existentes = await db.Calificaciones.Where(c => actividadIds.Contains(c.ActividadId)).ToListAsync(ct);
        var indice = existentes.ToDictionary(c => (c.ActividadId, c.EstudianteId));
        var vigentes = new HashSet<(int, int)>();

        foreach (var est in estudiantes)
        {
            var items = porUsuario.GetValueOrDefault(est.MoodleUserId) ?? new List<MoodleGradeItem>();
            foreach (var act in actividades)
            {
                // El cmid identifica la actividad sin ambigüedad; módulo + instancia solo se usa si Moodle no envía el cmid.
                // (Con una sola condición «O», un ítem anterior con la misma instancia podía ganarle al correcto.)
                var item = items.FirstOrDefault(i => i.CmId.HasValue && i.CmId.Value == act.MoodleCmId)
                    ?? items.FirstOrDefault(i => !i.CmId.HasValue &&
                        string.Equals(i.ItemModule, act.Modulo, StringComparison.OrdinalIgnoreCase) && i.ItemInstance == act.MoodleInstanceId);

                var bruta = item?.GradeRaw ?? GradeStateClassifier.ParseNotaFormateada(item?.GradeFormatted);
                var nota = GradeStateClassifier.NormalizarEscala(bruta, item?.GradeMax);
                var entregado = act.Modulo == "assign" && entregadas.Contains((act.MoodleInstanceId, est.MoodleUserId));
                var estado = GradeStateClassifier.Clasificar(nota, entregado);

                if (!indice.TryGetValue((act.Id, est.Id), out var fila))
                {
                    fila = new Calificacion { ActividadId = act.Id, EstudianteId = est.Id };
                    db.Calificaciones.Add(fila);
                }
                fila.Nota = nota;
                fila.Estado = estado;
                fila.FechaSincronizacion = ahora;
                vigentes.Add((act.Id, est.Id));
            }
        }

        // Estudiantes que dejaron de estar matriculados: Moodle es la fuente de verdad
        db.Calificaciones.RemoveRange(existentes.Where(c => !vigentes.Contains((c.ActividadId, c.EstudianteId))));
        await db.SaveChangesAsync(ct);
        return (vigentes.Count, entregasDisponibles);
    }

    private async Task<ResultadoSincronizacion> RegistrarAsync(int? materiaId, string tipo, bool ok, string mensaje,
        string email, DateTime fecha, int elementos, CancellationToken ct)
    {
        db.Sincronizaciones.Add(new SincronizacionLog
        {
            MateriaId = materiaId, Tipo = tipo, Exitosa = ok, Mensaje = Recortar(mensaje, 1000),
            UsuarioEmail = email, Fecha = fecha, Origen = moodle.BaseUrl
        });
        await db.SaveChangesAsync(ct);
        return new ResultadoSincronizacion(ok, mensaje, fecha, moodle.BaseUrl, elementos);
    }

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];

    private static string MensajeError(Exception ex) => ex switch
    {
        MoodleException me => $"Moodle respondió con error ({me.ErrorCode}): {me.Message}",
        TaskCanceledException => "Moodle no respondió a tiempo (timeout).",
        _ => $"No se pudo conectar con Moodle: {ex.Message}"
    };
}
