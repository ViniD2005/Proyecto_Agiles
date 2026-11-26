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
    private const int MaxHechosModelo = 1600, MaxHechosRespuesta = 14;

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

        // ---------- 3a) persona que no existe en el curso: se responde sin consultar al modelo (evita confundirla con otro estudiante)
        var desconocido = PlanificadorConsulta.NombreNoReconocido(pregunta, mencionados, propias.Select(p => p.Nombre));
        if (desconocido is not null)
        {
            var nombres = estudiantes.Select(e => $"{e.Nombres} {e.Apellidos}".Trim()).OrderBy(n => n).ToList();
            consulta.Resultado = "SinEstudiante"; // máx. 20 caracteres (columna Resultado)
            consulta.ModeloDisponible = false;
            consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
            await db.SaveChangesAsync(ct);
            await AuditarAsync(consulta, Array.Empty<(string, IReadOnlyDictionary<string, string>, ResultadoHerramienta, int)>(), ct);
            return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
                consulta.Id, req.MateriaId, materia.Nombre, "Estudiante no encontrado", Array.Empty<InvocacionDto>(),
                new[]
                {
                    $"No hay ningún estudiante llamado «{desconocido}» en «{materia.Nombre}».",
                    $"Estudiantes de la materia ({nombres.Count}): {string.Join(", ", nombres)}."
                },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "Revisa la ortografía o prueba solo con el nombre o el apellido." }, null, false,
                Array.Empty<string>(), consulta.FechaUtc,
                $"No encontré ningún estudiante llamado «{desconocido}» en «{materia.Nombre}». Revisa el nombre o prueba solo con el apellido."));
        }

        // ---------- 3b) saludo, agradecimiento o «¿qué puedes hacer?»: respuesta amable sin consultar datos ni modelo
        var charla = PlanificadorConsulta.RespuestaConversacional(pregunta);
        if (charla is not null)
        {
            consulta.Intencion = "Conversación";
            consulta.Resultado = "Conversacion";
            consulta.ModeloDisponible = false;
            consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
            await db.SaveChangesAsync(ct);
            await AuditarAsync(consulta, Array.Empty<(string, IReadOnlyDictionary<string, string>, ResultadoHerramienta, int)>(), ct);
            return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
                consulta.Id, req.MateriaId, materia.Nombre, "Conversación", Array.Empty<InvocacionDto>(),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, false,
                Array.Empty<string>(), consulta.FechaUtc, charla));
        }

        // ---------- 3c) pregunta ajena al curso: no se consulta el modelo ni se inventa una respuesta
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
                new[] { MensajeFueraDeAlcance }, consulta.FechaUtc, MensajeFueraDeAlcance));
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
        string? respuestaModelo = null;
        var modeloDisponible = false;

        try
        {
            var mensajes = ConstruirMensajes(preguntaAnonima, anon, seleccion);
            var llmResp = await llm.CompletarAsync(new LlmSolicitud(mensajes, SalidaJson: true), ct);
            var salida = SalidaModelo.ParsearChat(llmResp.Texto);
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
                if (!string.IsNullOrWhiteSpace(salida.Respuesta))
                    respuestaModelo = Filtrar(new[] { salida.Respuesta }, false).FirstOrDefault();
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
        var pideRecomendar = (plan.Enfoque & (Enfoque.Riesgo | Enfoque.Temas | Enfoque.Alertas | Enfoque.Resumen)) != 0;
        if (recomendaciones.Count == 0 && curso is not null && !preguntaDeEstudiante && pideRecomendar)
        {
            recomendaciones.AddRange(ReglasCurso.Recomendaciones(curso));
            if (modeloDisponible) advertencias.Add("El modelo no propuso recomendaciones válidas; se usaron las reglas del sistema.");
        }
        if (!modeloDisponible && inferencias.Count == 0)
            incertidumbre.Add("No hay inferencias del modelo: lo mostrado son solo datos calculados y reglas.");

        incertidumbre = incertidumbre.Distinct().ToList();

        // respuesta directa: la del modelo si superó la validación; si no, una armada por reglas con los mismos datos
        var respuesta = respuestaModelo ?? RespuestaPorReglas(plan.Enfoque, materia.Nombre, hechos,
            estudiantes.Select(e => $"{e.Nombres} {e.Apellidos}".Trim()).OrderBy(n => n).ToList());

        // ---------- 8) cierre: registro y auditoría
        consulta.Resultado = "Respondida";
        consulta.Modelo = modeloUsado is null ? null : Cortar(modeloUsado, 100);
        consulta.ModeloDisponible = modeloDisponible;
        consulta.DuracionMs = (int)reloj.ElapsedMilliseconds;
        await db.SaveChangesAsync(ct);
        await AuditarAsync(consulta, ejecuciones, ct);

        return Resultado<AgenteRespuestaDto>.Ok(new AgenteRespuestaDto(
            consulta.Id, req.MateriaId, materia.Nombre, plan.Intencion, invocaciones,
            hechos, inferencias, recomendaciones, incertidumbre, modeloUsado, modeloDisponible, advertencias, consulta.FechaUtc, respuesta));
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
        "Eres el asistente pedagógico de EduMonitor AI para docentes universitarios. Hablas en español, con tono cercano y profesional, como un colega que conoce el curso.\n" +
        "Reglas obligatorias:\n" +
        "1. Contesta DIRECTAMENTE lo que pregunta el docente usando únicamente los HECHOS entregados. Si los hechos no contienen lo que pregunta, dilo con claridad; no inventes.\n" +
        "2. Los estudiantes se identifican solo con códigos (E01, E02…). No inventes códigos, nombres ni cifras; cita las cifras tal como aparecen.\n" +
        "3. No afirmes tendencias, causas, asistencia ni «sin datos» que los HECHOS no indiquen.\n" +
        "4. Si la pregunta es sobre un estudiante, habla solo de ese estudiante.\n" +
        "5. Tus sugerencias son orientativas y las revisará el docente.\n" +
        "Devuelve SOLO un objeto JSON con esta forma exacta:\n" +
        "{\"respuesta\":\"...\",\"inferencias\":[],\"recomendaciones\":[],\"incertidumbre\":[]}\n" +
        "- respuesta: de 1 a 3 frases naturales que contestan la pregunta, sin repetir todos los hechos.\n" +
        "- inferencias: máximo 1, solo si añade algo que la respuesta no dice (puede ir vacío).\n" +
        "- recomendaciones: máximo 2, concretas, solo si la pregunta lo amerita (puede ir vacío si solo pidió un dato).\n" +
        "- incertidumbre: vacío, salvo que falte un dato que afecte la respuesta.";

    /// <summary>Respuesta directa armada por el código con los hechos seleccionados (cuando el modelo no responde o su texto no se valida).</summary>
    public static string RespuestaPorReglas(Enfoque enfoque, string materia, IReadOnlyList<string> hechosDocente, IReadOnlyList<string> nombresEstudiantes)
    {
        if (enfoque.HasFlag(Enfoque.Listado) && nombresEstudiantes.Count > 0)
            return $"En «{materia}» hay {nombresEstudiantes.Count} estudiante(s): {string.Join(", ", nombresEstudiantes.Take(40))}.";
        if (enfoque.HasFlag(Enfoque.Ranking))
        {
            var ranking = hechosDocente.Where(h => h.StartsWith("Mejores notas") || h.StartsWith("Notas ponderadas más bajas")).Take(2).ToList();
            if (ranking.Count > 0) return string.Join(" ", ranking);
        }
        if (hechosDocente.Count == 0) return "No encontré datos suficientes para responder esta consulta.";
        return string.Join(" ", hechosDocente.Take(2));
    }

    public static IReadOnlyList<LlmMensaje> ConstruirMensajes(
        string preguntaAnonima, Anonimizador anon, SelectorHechos.Seleccion seleccion)
    {
        var hechos = new StringBuilder();
        foreach (var linea in seleccion.Hechos)
            hechos.AppendLine("- " + anon.ParaModelo(linea));
        // Sin el detalle JSON: los hechos ya traen las cifras y el validador solo acepta cifras de los hechos, así que el JSON
        // solo alargaba el prompt (y la espera) sin aportar nada utilizable.
        var usuario =
            $"PREGUNTA DEL DOCENTE: {preguntaAnonima}\n\n" +
            $"HECHOS VERIFICADOS (calculados por el sistema):\n{CortarLineas(hechos.ToString(), MaxHechosModelo)}";

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

    /// <summary>Recorta por líneas completas para no dejar un hecho cortado a la mitad.</summary>
    private static string CortarLineas(string t, int max)
    {
        if (t.Length <= max) return t;
        var sb = new StringBuilder();
        foreach (var linea in t.Split('\n'))
        {
            if (sb.Length + linea.Length + 1 > max) break;
            sb.AppendLine(linea.TrimEnd('\r'));
        }
        return sb.ToString();
    }
}
