using System.Diagnostics;
using System.Text;
using EduMonitor.Analytics.Api.Agente.Herramientas;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Domain;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common.Auditoria;
using EduMonitor.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Agente;

/// <summary>
/// HU-13 / HU-14. Coordina una consulta del docente de punta a punta:
/// autorización por curso → anonimización → plan de herramientas → ejecución validada y registrada →
/// hechos calculados por el código → inferencias y recomendaciones del modelo (con respaldo por reglas) → incertidumbre.
/// Los HECHOS salen siempre de las herramientas (RN-09); el modelo solo aporta inferencias y recomendaciones (RN-10).
/// </summary>
public class AgenteOrquestador(
    AnalyticsDbContext db, ILlmProvider llm, RegistroHerramientas registro, AnalisisCursoService analisis,
    IAuditClient audit, ILogger<AgenteOrquestador> logger)
{
    public const int MaxPregunta = 500;
    private const int MaxHechosModelo = 3000, MaxDatosModelo = 3500, MaxHechosRespuesta = 14;

    public const string MensajeFueraDeAlcance =
        "Solo respondo consultas sobre el rendimiento de tus cursos: estudiantes, notas, riesgo, alertas, entregas, temas o recomendaciones. " +
        "Prueba, por ejemplo: «¿Qué estudiantes están en riesgo?», «¿Cómo va [nombre de un estudiante]?» o «¿Qué temas son más débiles?».";

    private static Resultado<AgenteRespuestaDto> Falla(CodigoResultado c, string m) => Resultado<AgenteRespuestaDto>.Falla(c, m);

    public async Task<Resultado<AgenteRespuestaDto>> ResponderAsync(string? emailDocente, ConsultaAgenteRequest? req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailDocente)) return Falla(CodigoResultado.Prohibido, "No se pudo identificar al docente.");
        if (req is null || string.IsNullOrWhiteSpace(req.Pregunta)) return Falla(CodigoResultado.Invalido, "Escribe una consulta para el agente.");
        var pregunta = req.Pregunta.Trim();
        if (pregunta.Length > MaxPregunta) return Falla(CodigoResultado.Invalido, $"La consulta no puede superar {MaxPregunta} caracteres.");
        var email = emailDocente.Trim().ToLowerInvariant();
        var reloj = Stopwatch.StartNew();

        // ---------- 1) autorización por curso (HU-14, criterio 4): antes de cualquier herramienta o llamada al modelo
        var materia = await db.Materias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.MateriaId, ct);
        if (materia is null)
            return Falla(CodigoResultado.NoEncontrado, "La materia no tiene datos de análisis todavía. Sincronice las calificaciones desde «Mis materias».");
        if (!string.Equals(materia.DocenteEmail, email, StringComparison.OrdinalIgnoreCase))
            return await DenegarAsync(email, req.MateriaId, "La materia consultada no pertenece al docente.",
                "No tienes permiso para consultar esta materia.", ct);

        var propias = await db.Materias.AsNoTracking().Where(m => m.DocenteEmail.ToLower() == email).ToListAsync(ct);
        var ajenas = await db.Materias.AsNoTracking().Where(m => m.DocenteEmail.ToLower() != email).Select(m => m.Nombre).ToListAsync(ct);
        var ajena = CursoAjenoMencionado(pregunta, propias.Select(p => p.Nombre).ToList(), ajenas);
        if (ajena is not null)
            return await DenegarAsync(email, req.MateriaId, $"La consulta menciona el curso «{ajena}», que no es del docente.",
                $"No puedo consultar «{ajena}»: no pertenece a tus cursos autorizados.", ct);

        // ---------- 2) anonimización (RN-11)
        var estudiantes = await db.Estudiantes.AsNoTracking().Where(e => e.MateriaId == req.MateriaId).ToListAsync(ct);
        var anon = new Anonimizador(estudiantes.Select(e => (e.EstudianteId, $"{e.Nombres} {e.Apellidos}".Trim())));
        var (preguntaAnonima, mencionados) = anon.AnonimizarPregunta(pregunta);

        // ---------- 3) plan y registro de la consulta
        var plan = PlanificadorConsulta.Planificar(preguntaAnonima, mencionados, req.MateriaId);
        var consulta = new ConsultaAgente
        {
            FechaUtc = DateTime.UtcNow, DocenteEmail = email, MateriaId = req.MateriaId,
            Pregunta = Cortar(AuditSanitizer.Limpiar(preguntaAnonima, 600) ?? string.Empty, 600),
            Intencion = Cortar(plan.Intencion, 100), Resultado = "Procesando"
        };
        db.ConsultasAgente.Add(consulta);
        await db.SaveChangesAsync(ct);

        // ---------- 3b) pregunta ajena al curso: no se consulta el modelo ni se inventa una respuesta
        if (plan.FueraDeAlcance)
        {
            consulta.Resultado = "FueraDeAlcance";
            consulta.ModeloDisponible = false;
            consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
            await db.SaveChangesAsync(ct);
            await AuditarAsync(consulta, Array.Empty<(string, IReadOnlyDictionary<string, string>, ResultadoHerramienta, int)>(), ct);
            return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
                consulta.Id, req.MateriaId, materia.Nombre, plan.Intencion, Array.Empty<InvocacionDto>(),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, false,
                new[] { MensajeFueraDeAlcance }, consulta.FechaUtc));
        }

        // ---------- 4) herramientas: parámetros validados, fallos controlados, todo registrado (HU-13, criterios 2 y 3)
        var contexto = new ContextoHerramienta(email, propias.Select(p => p.Id).ToHashSet());
        var ejecuciones = new List<(string Nombre, IReadOnlyDictionary<string, string> Args, ResultadoHerramienta Resultado, int Ms)>();
        foreach (var llamada in plan.Llamadas)
        {
            var sw = Stopwatch.StartNew();
            var resultado = await EjecutarAsync(llamada, contexto, ct);
            ejecuciones.Add((llamada.Nombre, llamada.Argumentos, resultado, (int)sw.ElapsedMilliseconds));
        }
        foreach (var x in ejecuciones)
            db.InvocacionesHerramienta.Add(new InvocacionHerramienta
            {
                ConsultaId = consulta.Id, FechaUtc = DateTime.UtcNow, Herramienta = x.Nombre,
                Parametros = Cortar(AuditSanitizer.Limpiar(HerramientasUtil.Serializar(x.Args), 500) ?? "{}", 500),
                Exito = x.Resultado.Exito, Resumen = Cortar(x.Resultado.Resumen, 500), DuracionMs = x.Ms
            });

        var exitosas = ejecuciones.Where(x => x.Resultado.Exito).ToList();
        var fallidas = ejecuciones.Where(x => !x.Resultado.Exito).ToList();
        var advertencias = new List<string>();
        foreach (var f in fallidas)
            advertencias.Add($"No pude obtener el dato de «{f.Nombre}»: {f.Resultado.Error}");

        string[] herramientasDeDatos = { "obtener_calificaciones", "identificar_alertas", "generar_resumen" };
        var hayDatos = exitosas.Any(x => herramientasDeDatos.Contains(x.Nombre));
        var invocaciones = ejecuciones.Select(x => new InvocacionDto(x.Nombre, x.Args, x.Resultado.Exito, x.Resultado.Resumen, x.Ms)).ToList();

        // ---------- 5) sin datos: no se inventa nada (HU-13, criterio 3)
        if (!hayDatos)
        {
            consulta.Resultado = "SinDatos";
            consulta.ModeloDisponible = false;
            consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
            await db.SaveChangesAsync(ct);
            await AuditarAsync(consulta, ejecuciones, ct);
            advertencias.Insert(0, "No pude obtener los datos del curso, así que no voy a responder con información inventada.");
            return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
                consulta.Id, req.MateriaId, materia.Nombre, plan.Intencion, invocaciones,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { "No hay datos disponibles para responder esta consulta." }, null, false, advertencias, consulta.FechaUtc));
        }

        // ---------- 6) hechos (código), incertidumbre (código) y respaldo por reglas
        var seleccion = SelectorHechos.Elegir(plan.Enfoque, exitosas.Select(x => (x.Nombre, x.Resultado)).ToList());
        var hechosConMarcas = seleccion.Hechos.Take(MaxHechosRespuesta).ToList();
        var hechos = hechosConMarcas.Select(anon.ParaDocente).ToList();

        var incertidumbre = new List<string>();
        var rAnalisis = await analisis.ObtenerAsync(req.MateriaId, email, ct);
        var curso = rAnalisis.EsOk ? rAnalisis.Valor : null;
        if (curso is not null) incertidumbre.AddRange(ReglasCurso.Incertidumbre(curso).Where(l => IncertidumbreAplica(plan.Enfoque, l)));
        foreach (var f in fallidas) incertidumbre.Add($"Falló «{f.Nombre}»: la respuesta puede estar incompleta.");

        // ---------- 7) modelo de lenguaje: inferencias y recomendaciones (con respaldo si no responde)
        var inferencias = new List<string>();
        var recomendaciones = new List<string>();
        string? modeloUsado = null;
        var modeloDisponible = false;

        try
        {
            var mensajes = ConstruirMensajes(preguntaAnonima, anon, seleccion);
            var respuesta = await llm.CompletarAsync(new LlmSolicitud(mensajes, SalidaJson: true), ct);
            var salida = SalidaModelo.ParsearChat(respuesta.Texto);
            if (salida is null)
            {
                advertencias.Add("El modelo devolvió un formato inesperado; se muestran solo hechos y recomendaciones basadas en reglas.");
            }
            else
            {
                modeloDisponible = true;
                modeloUsado = $"{llm.Nombre} · {llm.Modelo}";
                var descartadas = 0;
                var evidencia = string.Join("\n", hechosConMarcas.Select(anon.ParaModelo).Concat(incertidumbre));
                var sinDatos = curso?.Curso.SinDatos ?? 0;
                List<string> Filtrar(IEnumerable<string> lineas, bool esIncertidumbre)
                {
                    var ok = new List<string>();
                    foreach (var linea in lineas)
                    {
                        // cita un estudiante que no existe, o afirma algo que los hechos no respaldan
                        if (anon.TieneCodigoInventado(linea) || !ValidadorModelo.EstaRespaldada(linea, evidencia, sinDatos, esIncertidumbre))
                        { descartadas++; continue; }
                        ok.Add(anon.RestaurarParaDocente(linea));
                    }
                    return ok;
                }
                inferencias.AddRange(Filtrar(salida.Inferencias, false));
                recomendaciones.AddRange(Filtrar(salida.Recomendaciones, false));
                incertidumbre.AddRange(Filtrar(salida.Incertidumbre, true));
                if (descartadas > 0)
                    advertencias.Add($"Se descartaron {descartadas} afirmación(es) del modelo que los datos calculados no respaldan.");
            }
        }
        catch (LlmNoDisponibleException ex)
        {
            logger.LogWarning("El modelo no estuvo disponible para la consulta {Id}: {Mensaje}", consulta.Id, ex.Message);
            advertencias.Add("El modelo de lenguaje no está disponible en este momento. Se muestran los hechos calculados y recomendaciones basadas en reglas.");
        }

        var preguntaDeEstudiante = plan.Enfoque.HasFlag(Enfoque.Estudiante);
        if (recomendaciones.Count == 0 && curso is not null && !preguntaDeEstudiante)
        {
            recomendaciones.AddRange(ReglasCurso.Recomendaciones(curso));
            if (modeloDisponible) advertencias.Add("El modelo no propuso recomendaciones válidas; se usaron las reglas del sistema.");
        }
        if (!modeloDisponible && inferencias.Count == 0)
            incertidumbre.Add("No hay inferencias del modelo: lo mostrado son solo datos calculados y reglas.");

        incertidumbre = incertidumbre.Distinct().ToList();

        // ---------- 8) cierre: registro y auditoría
        consulta.Resultado = "Respondida";
        consulta.Modelo = modeloUsado is null ? null : Cortar(modeloUsado, 100);
        consulta.ModeloDisponible = modeloDisponible;
        consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
        await db.SaveChangesAsync(ct);
        await AuditarAsync(consulta, ejecuciones, ct);

        return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
            consulta.Id, req.MateriaId, materia.Nombre, plan.Intencion, invocaciones,
            hechos, inferencias, recomendaciones, incertidumbre, modeloUsado, modeloDisponible, advertencias, consulta.FechaUtc));
    }

    // ------------------------------------------------------------------ herramientas
    private async Task<ResultadoHerramienta> EjecutarAsync(LlamadaHerramienta llamada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var herramienta = registro.Obtener(llamada.Nombre);
        if (herramienta is null) return ResultadoHerramienta.Falla($"La herramienta «{llamada.Nombre}» no existe.");

        var (validos, error, denegado) = HerramientasUtil.Validar(herramienta, llamada.Argumentos, contexto);
        if (validos is null) return ResultadoHerramienta.Falla(error ?? "Parámetros inválidos.", denegado);

        try
        {
            return await herramienta.EjecutarAsync(contexto, validos, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló la herramienta {Herramienta}.", llamada.Nombre);
            return ResultadoHerramienta.Falla("error interno al ejecutar la herramienta.");
        }
    }

    // ------------------------------------------------------------------ mensajes para el modelo
    public const string InstruccionesSistema =
        "Eres el asistente pedagógico de EduMonitor AI para docentes universitarios. Responde siempre en español.\n" +
        "Reglas obligatorias:\n" +
        "1. Usa únicamente los DATOS entregados. Si un dato no aparece, indícalo; no inventes notas, estudiantes, temas ni porcentajes.\n" +
        "2. Los estudiantes se identifican solo con códigos (E01, E02…). No inventes códigos ni nombres.\n" +
        "3. Los números ya fueron calculados por el sistema: no los recalcules ni los cambies.\n" +
        "4. Tus sugerencias son orientativas y las revisará el docente.\n" +
        "5. Responde SOLO a la pregunta del docente: cada inferencia y recomendación debe tratar de lo preguntado. Si la pregunta es sobre un estudiante, habla únicamente de ese estudiante.\n" +
        "6. Cita las cifras tal como aparecen en los HECHOS. No afirmes tendencias, causas, asistencia ni «sin datos» que los HECHOS no indiquen.\n" +
        "Devuelve SOLO un objeto JSON con esta forma exacta:\n" +
        "{\"inferencias\":[\"...\"],\"recomendaciones\":[\"...\"],\"incertidumbre\":[\"...\"]}\n" +
        "- inferencias: interpretaciones razonables de los datos (máximo 3); cada una indica en qué dato se apoya.\n" +
        "- recomendaciones: acciones concretas para el docente (máximo 3).\n" +
        "- incertidumbre: límites o datos faltantes que afectan la respuesta (puede ir vacío).";

    public static IReadOnlyList<LlmMensaje> ConstruirMensajes(
        string preguntaAnonima, Anonimizador anon, SelectorHechos.Seleccion seleccion)
    {
        var hechos = new StringBuilder();
        foreach (var linea in seleccion.Hechos)
            hechos.AppendLine("- " + anon.ParaModelo(linea));
        var datos = new StringBuilder();
        foreach (var d in seleccion.Datos)
            datos.AppendLine(anon.ParaModelo(d));

        var usuario =
            $"PREGUNTA DEL DOCENTE: {preguntaAnonima}\n\n" +
            $"HECHOS VERIFICADOS (calculados por el sistema):\n{Cortar(hechos.ToString(), MaxHechosModelo)}\n" +
            $"DETALLE (JSON):\n{Cortar(datos.ToString(), MaxDatosModelo)}";

        return new[] { new LlmMensaje(LlmMensaje.Sistema, InstruccionesSistema), new LlmMensaje(LlmMensaje.Usuario, usuario) };
    }

    /// <summary>Muestra solo los límites de datos que afectan a lo preguntado.</summary>
    private static bool IncertidumbreAplica(Enfoque enfoque, string linea)
    {
        var l = Anonimizador.Normalizar(linea);
        if (enfoque.HasFlag(Enfoque.Resumen)) return true;
        if (l.Contains("peso") || l.Contains("sin datos") || l.Contains("no tienen calificaciones") || l.Contains("pendientes de calificar")) return true;
        if (l.Contains("tema") || l.Contains("mapear") || l.Contains("estructura")) return enfoque.HasFlag(Enfoque.Temas) || enfoque.HasFlag(Enfoque.Riesgo);
        return !enfoque.HasFlag(Enfoque.Estudiante);
    }

    // ------------------------------------------------------------------ cursos de otros docentes
    /// <summary>
    /// Devuelve el nombre de un curso de otro docente si la consulta lo menciona. Si el nombre ajeno está contenido en el de un
    /// curso propio (p. ej. «Base de Datos» dentro de «Base de Datos II») no se considera ajeno.
    /// </summary>
    public static string? CursoAjenoMencionado(string pregunta, IReadOnlyList<string> propias, IReadOnlyList<string> ajenas)
    {
        var q = Anonimizador.Normalizar(pregunta);
        var propiasN = propias.Select(Anonimizador.Normalizar).ToList();
        foreach (var ajena in ajenas)
        {
            var n = Anonimizador.Normalizar(ajena).Trim();
            if (n.Length < 5 || !q.Contains(n)) continue;
            if (propiasN.Any(p => p.Contains(n))) continue;
            return ajena;
        }
        return null;
    }

    // ------------------------------------------------------------------ denegación y auditoría
    private async Task<Resultado<AgenteRespuestaDto>> DenegarAsync(string email, int materiaId, string motivo, string mensaje, CancellationToken ct)
    {
        db.ConsultasAgente.Add(new ConsultaAgente
        {
            FechaUtc = DateTime.UtcNow, DocenteEmail = email, MateriaId = materiaId,
            Pregunta = "(no registrada: acceso denegado)", Intencion = "Denegada", Resultado = "Denegada"
        });
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("AGENTE_ACCESO_DENEGADO", "MS-C", $"{motivo} (materia {materiaId})", ct);
        return Falla(CodigoResultado.Prohibido, mensaje);
    }

    private async Task AuditarAsync(
        ConsultaAgente consulta, IReadOnlyList<(string Nombre, IReadOnlyDictionary<string, string> Args, ResultadoHerramienta Resultado, int Ms)> ejecuciones, CancellationToken ct)
    {
        var tareas = new List<Task>
        {
            audit.RegistrarAsync("AGENTE_CONSULTA", "MS-C",
                $"Consulta {consulta.Id} · materia {consulta.MateriaId} · intención «{consulta.Intencion}» · {ejecuciones.Count} herramienta(s) · {consulta.Resultado}", ct)
        };
        tareas.AddRange(ejecuciones.Select(x => audit.RegistrarAsync("AGENTE_HERRAMIENTA", "MS-C",
            $"Consulta {consulta.Id} · {x.Nombre} · {(x.Resultado.Exito ? "OK" : "FALLO")} · {x.Ms} ms", ct)));
        await Task.WhenAll(tareas);
    }

    private static string Cortar(string t, int max) => t.Length <= max ? t : t[..max];
}
