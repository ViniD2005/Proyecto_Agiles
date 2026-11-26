using System.Text.RegularExpressions;

namespace EduMonitor.Analytics.Api.Agente;

public record LlamadaHerramienta(string Nombre, IReadOnlyDictionary<string, string> Argumentos);

/// <summary>Sobre qué trata la pregunta. Decide qué hechos se muestran y qué datos recibe el modelo.</summary>
[Flags]
public enum Enfoque
{
    Ninguno = 0, Estudiante = 1, Riesgo = 2, Temas = 4, Alertas = 8, Calificaciones = 16, Resumen = 32, Entregas = 64, Listado = 128, Ranking = 256
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

    private static readonly Regex DespuesDeDisparador = new(
        @"(?i:\b(?:existe|existen|como va|c[oó]mo va|como est[aá]|c[oó]mo est[aá]|como le va|c[oó]mo le va|estudiante|alumn[oa]|compa[ñn]er[oa])\b)\s+(?<resto>[^?¿!.,;:\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SoloLetras = new(@"^\p{L}{3,}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Rellenos = new()
    {
        "el", "la", "los", "las", "un", "una", "unos", "unas", "mi", "mis", "nuestro", "nuestra", "algun", "alguna", "alguno", "ningun", "ninguna",
        "llamado", "llamada", "nombre", "estudiante", "alumno", "alumna", "companero", "companera", "que", "se", "de"
    };
    private static readonly HashSet<string> Cortes = new()
    {
        "en", "con", "por", "para", "hoy", "este", "esta", "ese", "esa", "tiene", "tienen", "esta", "estan", "son", "es", "mas", "y", "o", "existe",
        "va", "como", "cuando", "donde", "porque", "sobre", "del", "al"
    };
    // palabras del dominio y generales: si el «nombre» las contiene, la pregunta trata del curso y no de una persona
    private static readonly string[] NoNombre =
    {
        "curso", "materia", "estudiante", "alumno", "clase", "parcial", "docente", "grupo", "resumen", "recomend", "rendimiento", "general",
        "riesgo", "critic", "umbral", "alerta", "nota", "calific", "promedio", "tema", "contenido", "unidad", "entrega", "actividad", "tarea",
        "trabajo", "evaluacion", "examen", "prueba", "apoyo", "tutoria", "moodle", "semana", "hoy", "algo", "alguien", "nadie", "todos", "todo",
        "importante", "bien", "mal", "mejor", "peor", "aprob", "reprob", "pendiente", "reporte", "informe", "tendencia"
    };

    /// <summary>
    /// Si el docente pregunta por una persona («existe el estudiante vini arias», «¿cómo va Pedro Pérez?») y ese nombre no
    /// corresponde a ningún estudiante del curso, devuelve el nombre escrito. Así el agente responde «no existe» sin llamar al
    /// modelo y sin arriesgar que lo confunda con otro estudiante. Funciona con minúsculas y sin tildes.
    /// </summary>
    private static readonly Regex Saludo = new(@"^\s*(hola|holi|buenas|buenos dias|buen dia|buenas tardes|buenas noches|hey|saludos|que tal|como estas)\b", RegexOptions.Compiled);
    private static readonly Regex Agradece = new(@"^\s*(gracias|muchas gracias|ok gracias|perfecto|listo|genial|excelente)\b", RegexOptions.Compiled);
    private static readonly Regex PideAyuda = new(@"(que puedes hacer|que sabes hacer|en que (me )?puedes ayudar|como funcionas|quien eres|que eres|ayuda)", RegexOptions.Compiled);

    /// <summary>Respuesta fija para saludos, agradecimientos y «¿qué puedes hacer?». Null si es una consulta del curso.</summary>
    public static string? RespuestaConversacional(string pregunta)
    {
        var q = Regex.Replace(Anonimizador.Normalizar(pregunta), @"[^\p{L}\p{N} ]+", " ").Trim();
        if (q.Length == 0 || q.Length > 60) return null;
        const string Ayuda = "Puedo ayudarte con tu curso: ver quiénes están en riesgo y por qué, listar estudiantes y notas, revisar temas débiles, " +
            "entregas pendientes, alertas, la ficha de un estudiante o un resumen del curso. Prueba, por ejemplo: «¿Qué estudiantes están en riesgo?» o «¿Cómo va Joel Herrera?».";
        if (PideAyuda.IsMatch(q)) return "Soy el asistente de EduMonitor AI. " + Ayuda;
        if (Saludo.IsMatch(q) && !PalabrasCurso.Any(p => q.Contains(p)) ) return "¡Hola! Soy el asistente de EduMonitor AI. " + Ayuda;
        if (Agradece.IsMatch(q)) return "¡Con gusto! Si necesitas revisar otro aspecto del curso, solo pregúntame.";
        return null;
    }

    public static string? NombreNoReconocido(string pregunta, IReadOnlyList<int> mencionados, IEnumerable<string> nombresMaterias)
    {
        if (mencionados.Count > 0) return null;
        var materias = Anonimizador.Normalizar(string.Join(" ", nombresMaterias));
        foreach (Match m in DespuesDeDisparador.Matches(pregunta))
        {
            var palabras = new List<string>();
            foreach (var original in m.Groups["resto"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Anonimizador.Normalizar(original.Trim());
                if (palabras.Count == 0 && Rellenos.Contains(p)) continue;
                if (Cortes.Contains(p) || !SoloLetras.IsMatch(p)) break;
                palabras.Add(original.Trim());
                if (palabras.Count == 3) break;
            }
            if (palabras.Count == 0) continue;
            var normal = palabras.Select(Anonimizador.Normalizar).ToList();
            if (normal.Any(p => NoNombre.Any(k => p.Contains(k)) || (p.Length >= 4 && materias.Contains(p)))) continue;
            return string.Join(" ", palabras.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        }
        return null;
    }

    private static readonly string[] PalabrasRiesgo = { "riesgo", "critic", "reprob", "peligro", "perder", "preocup", "bajo ", "bajas", "umbral", "rendimiento" };
    private static readonly string[] PalabrasTema = { "tema", "contenido", "unidad", "debil", "dificult", "falla", "refuerz", "reforz" };
    private static readonly string[] PalabrasAlerta = { "alerta", "descenso", "evidencia" };
    private static readonly string[] PalabrasEntrega = { "entrega", "entregado", "cero", "pendiente", "actividad", "tarea", "trabajo", "prueba", "examen", "evaluacion" };
    private static readonly string[] PalabrasListado =
    {
        "estudiantes existen", "estudiantes hay", "alumnos hay", "alumnos existen", "quienes son", "quienes estan", "lista", "listado", "nombres",
        "cuantos estudiantes", "cuantos alumnos", "cuantos hay", "matriculad", "integrantes", "existen"
    };
    private static readonly string[] PalabrasRanking =
    {
        " mejor ", " mejores ", "mas alta", "mas altas", "nota maxima", "destac", "ranking", " peor ", " peores ", "mas baja", "mas bajas",
        "nota minima", "mayor nota", "menor nota", " top "
    };
    private static readonly string[] PalabrasNota = { "promedio", "nota", "calificac", "media", "desviacion", "estadistic", "aprob" };
    private static readonly string[] PalabrasCurso =
    {
        "curso", "materia", "estudiante", "alumno", "clase", "parcial", "docente", "grupo", "resumen", "recomend",
        "como va", "como vamos", "como esta", "seguimiento", "tutoria", "desempeno", "semestre", "general"
    };

    public static PlanConsulta Planificar(string preguntaAnonimizada, IReadOnlyList<int> estudiantesMencionados, int materiaId)
    {
        var q = " " + System.Text.RegularExpressions.Regex.Replace(Anonimizador.Normalizar(preguntaAnonimizada), @"[^\p{L}\p{N}]+", " ") + " ";
        bool Tiene(string[] palabras) => palabras.Any(p => q.Contains(p));

        var riesgo = Tiene(PalabrasRiesgo);
        var tema = Tiene(PalabrasTema);
        var alerta = Tiene(PalabrasAlerta);
        var entrega = Tiene(PalabrasEntrega);
        var nota = Tiene(PalabrasNota);
        var estudiante = estudiantesMencionados.Count > 0;
        var delCurso = Tiene(PalabrasCurso);
        var especifica = riesgo || tema || alerta || entrega || nota;
        var listado = Tiene(PalabrasListado) && !especifica;
        var ranking = Tiene(PalabrasRanking) && !tema && !entrega;

        var enfoque = Enfoque.Ninguno;
        var etiquetas = new List<string>();
        if (estudiante) { etiquetas.Add("Estudiante"); enfoque |= Enfoque.Estudiante; }
        if (riesgo) { etiquetas.Add("Riesgo"); enfoque |= Enfoque.Riesgo; }
        if (tema) { etiquetas.Add("Temas"); enfoque |= Enfoque.Temas; }
        if (alerta) { etiquetas.Add("Alertas"); enfoque |= Enfoque.Alertas; }
        if (entrega) { etiquetas.Add("Entregas"); enfoque |= Enfoque.Entregas; }
        if (nota) { etiquetas.Add("Calificaciones"); enfoque |= Enfoque.Calificaciones; }
        if (listado) { etiquetas.Add("Listado"); enfoque |= Enfoque.Listado; }
        if (ranking) { etiquetas.Add("Ranking"); enfoque |= Enfoque.Ranking; }

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
