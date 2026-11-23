using System.Text.Encodings.Web;
using System.Text.Json;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Recomendaciones;

/// <summary>
/// HU-15: genera, guarda con evidencia y fecha, permite editar y aprobar la recomendación de cada estudiante.
/// Las falencias, la evidencia y los contenidos a repasar los calcula el código; el modelo (si responde) propone las
/// actividades de refuerzo. Si el modelo falla se usa la plantilla por nivel de riesgo.
/// </summary>
public class RecomendacionService(
    AnalyticsDbContext db, AnaliticaService analitica, ILlmProvider llm, IAuditClient audit, ILogger<RecomendacionService> logger)
{
    public const string Aviso = "Recomendación orientativa: el docente la revisa y la aprueba; nada se envía de forma automática.";
    public const int MaxItems = 8, MaxLongitudItem = 400;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ------------------------------------------------------------------ acceso (RN-07)
    internal async Task<Resultado<MateriaAnalitica>> AutorizarAsync(int materiaId, string? email, CancellationToken ct)
    {
        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materiaId, ct);
        if (materia is null)
            return Resultado<MateriaAnalitica>.Falla(CodigoResultado.NoEncontrado,
                "La materia no tiene datos de análisis todavía. Sincronice las calificaciones desde «Mis materias».");
        if (string.IsNullOrWhiteSpace(email) || !string.Equals(materia.DocenteEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Resultado<MateriaAnalitica>.Falla(CodigoResultado.Prohibido, "No tienes permiso para esta materia.");
        return Resultado<MateriaAnalitica>.Ok(materia);
    }

    private async Task<string> NombreAsync(int materiaId, int estudianteId, CancellationToken ct)
    {
        var e = await db.Estudiantes.AsNoTracking().FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        var n = e is null ? string.Empty : $"{e.Nombres} {e.Apellidos}".Trim();
        return n.Length > 0 ? n : $"Estudiante {estudianteId}";
    }

    // ------------------------------------------------------------------ lectura
    public async Task<Resultado<RecomendacionesCursoDto>> ListarAsync(int materiaId, string? email, CancellationToken ct = default)
    {
        var acceso = await AutorizarAsync(materiaId, email, ct);
        if (!acceso.EsOk) return Resultado<RecomendacionesCursoDto>.Falla(acceso.Codigo, acceso.Mensaje!);

        var riesgo = await analitica.ObtenerRiesgoAsync(materiaId, email, ct);
        if (!riesgo.EsOk) return Resultado<RecomendacionesCursoDto>.Falla(riesgo.Codigo, riesgo.Mensaje ?? "Error.");
        var existentes = (await db.Recomendaciones.AsNoTracking().Where(r => r.MateriaId == materiaId).ToListAsync(ct))
            .ToDictionary(r => r.EstudianteId);

        var lista = riesgo.Valor!.Estudiantes.Select(e =>
        {
            existentes.TryGetValue(e.EstudianteId, out var r);
            return new RecomendacionResumenDto(e.EstudianteId, e.Nombre, e.Nivel, e.NotaPonderada, r?.Estado, r?.Fuente, r?.GeneradaUtc);
        }).ToList();
        return Resultado<RecomendacionesCursoDto>.Ok(new RecomendacionesCursoDto(materiaId, acceso.Valor!.Nombre, Aviso, lista));
    }

    public async Task<Resultado<RecomendacionDto>> ObtenerAsync(int materiaId, int estudianteId, string? email, CancellationToken ct = default)
    {
        var acceso = await AutorizarAsync(materiaId, email, ct);
        if (!acceso.EsOk) return Resultado<RecomendacionDto>.Falla(acceso.Codigo, acceso.Mensaje!);
        var r = await db.Recomendaciones.AsNoTracking().FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        if (r is null)
            return Resultado<RecomendacionDto>.Falla(CodigoResultado.NoEncontrado, "Este estudiante todavía no tiene una recomendación generada.");
        return Resultado<RecomendacionDto>.Ok(ADto(r, await NombreAsync(materiaId, estudianteId, ct)));
    }

    // ------------------------------------------------------------------ generar
    public async Task<Resultado<RecomendacionDto>> GenerarAsync(int materiaId, int estudianteId, string? email, CancellationToken ct = default)
    {
        var ficha = await analitica.ObtenerFichaAsync(materiaId, estudianteId, email, ct);   // valida acceso y pertenencia
        if (!ficha.EsOk) return Resultado<RecomendacionDto>.Falla(ficha.Codigo, ficha.Mensaje ?? "Error.");
        var f = ficha.Valor!;

        if (f.Nivel == nameof(NivelRiesgo.SinDatos))
            return Resultado<RecomendacionDto>.Falla(CodigoResultado.Invalido,
                "El estudiante aún no tiene calificaciones evaluadas: no hay base para recomendar.");

        var existente = await db.Recomendaciones.FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        if (existente is { Estado: EstadosRecomendacion.Aprobada })
            return Resultado<RecomendacionDto>.Falla(CodigoResultado.Conflicto,
                "Ya existe una recomendación aprobada. Si necesitas cambiarla, edítala (volverá a borrador) en lugar de regenerarla.");

        var mapa = await analitica.ObtenerMapaCalorAsync(materiaId, email, ct);
        var perfil = PerfilEstudianteBuilder.Construir(f, mapa.EsOk ? mapa.Valor : null);
        var evidencia = EvidenciaRecomendacion.Construir(perfil);
        var contenido = PlantillasRecomendacion.Generar(perfil);
        var fuente = FuentesRecomendacion.Plantilla;

        try
        {
            var respuesta = await llm.CompletarAsync(new LlmSolicitud(PromptsRecomendacion.Construir(perfil, evidencia), SalidaJson: true), ct);
            var salida = PromptsRecomendacion.Parsear(respuesta.Texto);
            if (salida is not null)
            {
                contenido = contenido with { ActividadesRefuerzo = salida.Value.Actividades, Observacion = salida.Value.Observacion ?? contenido.Observacion };
                fuente = FuentesRecomendacion.Modelo;
            }
            else logger.LogWarning("El modelo devolvió un formato inesperado para la recomendación; se usa la plantilla.");
        }
        catch (LlmNoDisponibleException ex)
        {
            logger.LogWarning("Modelo no disponible; se usa la plantilla por nivel de riesgo: {Mensaje}", ex.Message);
        }

        var ahora = DateTime.UtcNow;
        var guardada = existente ?? new RecomendacionEstudiante { MateriaId = materiaId, EstudianteId = estudianteId };
        guardada.Nivel = f.Nivel;
        guardada.NotaPonderada = f.NotaPonderada;
        guardada.Evidencia = evidencia;
        guardada.Contenido = JsonSerializer.Serialize(contenido, Json);
        guardada.Fuente = fuente;
        guardada.Estado = EstadosRecomendacion.Borrador;
        guardada.GeneradaUtc = ahora;                     // HU-15, criterio 4: conserva su fecha de generación
        guardada.GeneradaPor = email!.Trim().ToLowerInvariant();
        guardada.EditadaUtc = null; guardada.EditadaPor = null;
        guardada.AprobadaUtc = null; guardada.AprobadaPor = null;
        guardada.EnviadaUtc = null; guardada.EnviadaA = null;
        if (existente is null) db.Recomendaciones.Add(guardada);
        await db.SaveChangesAsync(ct);

        await audit.RegistrarAsync("RECOMENDACION_GENERADA", "MS-C",
            $"Materia {materiaId} · estudiante {estudianteId} · nivel {f.Nivel} · fuente {fuente}", ct);
        return Resultado<RecomendacionDto>.Ok(ADto(guardada, f.Nombre));
    }

    // ------------------------------------------------------------------ editar
    public async Task<Resultado<RecomendacionDto>> EditarAsync(
        int materiaId, int estudianteId, EditarRecomendacionRequest? req, string? email, CancellationToken ct = default)
    {
        var acceso = await AutorizarAsync(materiaId, email, ct);
        if (!acceso.EsOk) return Resultado<RecomendacionDto>.Falla(acceso.Codigo, acceso.Mensaje!);
        if (req is null) return Resultado<RecomendacionDto>.Falla(CodigoResultado.Invalido, "Faltan los datos de la recomendación.");

        var r = await db.Recomendaciones.FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        if (r is null) return Resultado<RecomendacionDto>.Falla(CodigoResultado.NoEncontrado, "Primero genera la recomendación de este estudiante.");

        var falencias = Limpiar(req.Falencias);
        var actividades = Limpiar(req.ActividadesRefuerzo);
        var repasar = Limpiar(req.ContenidosARepasar);
        var error = Validar(falencias, actividades, repasar, req.Observacion);
        if (error is not null) return Resultado<RecomendacionDto>.Falla(CodigoResultado.Invalido, error);

        var observacion = string.IsNullOrWhiteSpace(req.Observacion) ? null : Recortar(req.Observacion.Trim());
        r.Contenido = JsonSerializer.Serialize(new ContenidoRecomendacionDto(falencias, actividades, repasar, observacion), Json);
        r.Estado = EstadosRecomendacion.Borrador;      // al editar se vuelve a revisar y aprobar
        r.EditadaUtc = DateTime.UtcNow;
        r.EditadaPor = email!.Trim().ToLowerInvariant();
        r.AprobadaUtc = null; r.AprobadaPor = null;
        await db.SaveChangesAsync(ct);

        await audit.RegistrarAsync("RECOMENDACION_EDITADA", "MS-C", $"Materia {materiaId} · estudiante {estudianteId}", ct);
        return Resultado<RecomendacionDto>.Ok(ADto(r, await NombreAsync(materiaId, estudianteId, ct)));
    }

    // ------------------------------------------------------------------ aprobar
    public async Task<Resultado<RecomendacionDto>> AprobarAsync(int materiaId, int estudianteId, string? email, CancellationToken ct = default)
    {
        var acceso = await AutorizarAsync(materiaId, email, ct);
        if (!acceso.EsOk) return Resultado<RecomendacionDto>.Falla(acceso.Codigo, acceso.Mensaje!);
        var r = await db.Recomendaciones.FirstOrDefaultAsync(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId, ct);
        if (r is null) return Resultado<RecomendacionDto>.Falla(CodigoResultado.NoEncontrado, "Primero genera la recomendación de este estudiante.");

        if (r.Estado != EstadosRecomendacion.Aprobada)
        {
            r.Estado = EstadosRecomendacion.Aprobada;
            r.AprobadaUtc = DateTime.UtcNow;
            r.AprobadaPor = email!.Trim().ToLowerInvariant();
            await db.SaveChangesAsync(ct);
            await audit.RegistrarAsync("RECOMENDACION_APROBADA", "MS-C", $"Materia {materiaId} · estudiante {estudianteId}", ct);
        }
        return Resultado<RecomendacionDto>.Ok(ADto(r, await NombreAsync(materiaId, estudianteId, ct)));
    }

    // ------------------------------------------------------------------ utilidades
    internal static RecomendacionDto ADto(RecomendacionEstudiante r, string nombre)
    {
        ContenidoRecomendacionDto contenido;
        try { contenido = JsonSerializer.Deserialize<ContenidoRecomendacionDto>(r.Contenido, Json)!; }
        catch (JsonException) { contenido = new ContenidoRecomendacionDto(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null); }
        return new RecomendacionDto(
            r.Id, r.MateriaId, r.EstudianteId, nombre, r.Nivel, r.NotaPonderada, r.Evidencia, contenido, r.Fuente, r.Estado,
            r.GeneradaUtc, r.EditadaUtc, r.EditadaPor, r.AprobadaUtc, r.AprobadaPor, r.EnviadaUtc, r.EnviadaA, Aviso);
    }

    internal static ContenidoRecomendacionDto LeerContenido(RecomendacionEstudiante r) => ADto(r, string.Empty).Contenido;

    private static string Recortar(string t) => t.Length <= MaxLongitudItem ? t : t[..MaxLongitudItem];

    private static List<string> Limpiar(IEnumerable<string>? items) =>
        (items ?? Array.Empty<string>()).Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).Select(Recortar).Distinct().ToList();

    private static string? Validar(List<string> falencias, List<string> actividades, List<string> repasar, string? observacion)
    {
        if (falencias.Count == 0 && actividades.Count == 0)
            return "La recomendación debe tener al menos una falencia o una actividad de refuerzo.";
        if (falencias.Count > MaxItems || actividades.Count > MaxItems || repasar.Count > MaxItems)
            return $"Cada sección admite hasta {MaxItems} elementos.";
        if (observacion is not null && observacion.Length > MaxLongitudItem * 2)
            return "La observación es demasiado larga.";
        return null;
    }
}
