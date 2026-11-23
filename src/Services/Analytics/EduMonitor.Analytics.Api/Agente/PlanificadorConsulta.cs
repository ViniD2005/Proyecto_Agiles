namespace EduMonitor.Analytics.Api.Agente;

public record LlamadaHerramienta(string Nombre, IReadOnlyDictionary<string, string> Argumentos);

/// <summary>Sobre qué trata la pregunta. Decide qué hechos se muestran y qué datos recibe el modelo.</summary>
[Flags]
public enum Enfoque
{
    Ninguno = 0, Estudiante = 1, Riesgo = 2, Temas = 4, Alertas = 8, Calificaciones = 16, Resumen = 32, Entregas = 64
}

public record PlanConsulta(
    string Intencion, IReadOnlyList<LlamadaHerramienta> Llamadas, Enfoque Enfoque = Enfoque.Resumen, bool FueraDeAlcance = false);

/// <summary>
/// Decide qué herramientas ejecutar según la consulta. Es un enrutador de reglas y no el modelo: el modelo de lenguaje
/// nunca elige acciones ni parámetros, así que no puede salirse de la lista de herramientas ni del curso autorizado (RN-07).
/// Toda consulta del curso usa al menos 4 herramientas reales (HU-13, criterio 1). Una pregunta ajena al curso no se responde.
/// </summary>
public static class PlanificadorConsulta
{
    public const string IntencionFueraDeAlcance = "Fuera de alcance";

    private static readonly string[] PalabrasRiesgo = { "riesgo", "critic", "reprob", "peligro", "perder", "preocup", "bajo ", "bajas", "umbral", "rendimiento" };
    private static readonly string[] PalabrasTema = { "tema", "contenido", "unidad", "debil", "dificult", "falla", "refuerz", "reforz" };
    private static readonly string[] PalabrasAlerta = { "alerta", "descenso", "evidencia" };
    private static readonly string[] PalabrasEntrega = { "entrega", "entregado", "cero", "pendiente", "actividad", "tarea", "trabajo", "prueba", "examen", "evaluacion" };
    private static readonly string[] PalabrasNota = { "promedio", "nota", "calificac", "media", "desviacion", "estadistic", "aprob" };
    private static readonly string[] PalabrasCurso =
    {
        "curso", "materia", "estudiante", "alumno", "clase", "parcial", "docente", "grupo", "resumen", "recomend",
        "como va", "como vamos", "como esta", "seguimiento", "tutoria", "desempeno", "semestre", "general"
    };

    public static PlanConsulta Planificar(string preguntaAnonimizada, IReadOnlyList<int> estudiantesMencionados, int materiaId)
    {
        var q = " " + Anonimizador.Normalizar(preguntaAnonimizada) + " ";
        bool Tiene(string[] palabras) => palabras.Any(p => q.Contains(p));

        var riesgo = Tiene(PalabrasRiesgo);
        var tema = Tiene(PalabrasTema);
        var alerta = Tiene(PalabrasAlerta);
        var entrega = Tiene(PalabrasEntrega);
        var nota = Tiene(PalabrasNota);
        var estudiante = estudiantesMencionados.Count > 0;
        var delCurso = Tiene(PalabrasCurso);

        var enfoque = Enfoque.Ninguno;
        var etiquetas = new List<string>();
        if (estudiante) { etiquetas.Add("Estudiante"); enfoque |= Enfoque.Estudiante; }
        if (riesgo) { etiquetas.Add("Riesgo"); enfoque |= Enfoque.Riesgo; }
        if (tema) { etiquetas.Add("Temas"); enfoque |= Enfoque.Temas; }
        if (alerta) { etiquetas.Add("Alertas"); enfoque |= Enfoque.Alertas; }
        if (entrega) { etiquetas.Add("Entregas"); enfoque |= Enfoque.Entregas; }
        if (nota) { etiquetas.Add("Calificaciones"); enfoque |= Enfoque.Calificaciones; }

        // sin ninguna palabra del dominio ni estudiante: la pregunta no trata del curso
        var fuera = enfoque == Enfoque.Ninguno && !delCurso;
        if (enfoque == Enfoque.Ninguno) { etiquetas.Add(fuera ? IntencionFueraDeAlcance : "Resumen"); enfoque = Enfoque.Resumen; }

        var m = materiaId.ToString();
        string? nivel = q.Contains("critic") ? "Critico" : q.Contains("umbral") ? "EnUmbral" : null;

        var alertas = new Dictionary<string, string> { ["materiaId"] = m };
        if (nivel is not null) alertas["nivel"] = nivel;

        var llamadas = new List<LlamadaHerramienta>
        {
            new("listar_cursos_autorizados", new Dictionary<string, string>()),
            new("obtener_calificaciones", new Dictionary<string, string> { ["materiaId"] = m }),
            new("identificar_alertas", alertas),
            new("generar_resumen", new Dictionary<string, string> { ["materiaId"] = m })
        };
        if (riesgo || tema)
            llamadas.Add(new("obtener_desempeno_temas", new Dictionary<string, string> { ["materiaId"] = m }));
        foreach (var id in estudiantesMencionados.Take(3))
            llamadas.Add(new("obtener_ficha_estudiante", new Dictionary<string, string> { ["materiaId"] = m, ["estudianteId"] = id.ToString() }));

        return new PlanConsulta(string.Join(" + ", etiquetas), llamadas, enfoque, fuera);
    }
}
