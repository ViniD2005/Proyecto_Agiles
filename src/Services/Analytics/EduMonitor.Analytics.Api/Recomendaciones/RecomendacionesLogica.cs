using System.Text.RegularExpressions;
using EduMonitor.Analytics.Api.Agente;
using EduMonitor.Analytics.Api.Agente.Llm;
using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Services;

namespace EduMonitor.Analytics.Api.Recomendaciones;

public record TemaDebil(string Tema, string Unidad, decimal Nota, string Nivel);
public record ContenidoDebil(string Tema, string Contenido, decimal Nota);
public record NotaEvidencia(string Actividad, decimal Nota, string Estado);

/// <summary>Todo lo que el sistema sabe de un estudiante para recomendar. Se construye con datos ya calculados (RN-09).</summary>
public record PerfilEstudiante(
    int EstudianteId, string Nivel, decimal? NotaPonderada, decimal PesoEvaluado, decimal Critico, decimal Aprobado,
    IReadOnlyList<TemaDebil> TemasDebiles, IReadOnlyList<ContenidoDebil> ContenidosDebiles,
    IReadOnlyList<NotaEvidencia> Notas, IReadOnlyList<string> ActividadesSinEntrega,
    IReadOnlyList<string> Alertas, string? Tendencia);

public static class PerfilEstudianteBuilder
{
    public const int MaxTemas = 4, MaxContenidos = 6;

    public static PerfilEstudiante Construir(FichaEstudianteDto ficha, MapaCalorDto? mapa)
    {
        var debiles = ficha.Temas
            .Where(t => t.Nota.HasValue && t.Nivel != "Aprobado")
            .OrderBy(t => t.Nota).ThenBy(t => t.Tema).Take(MaxTemas)
            .Select(t => new TemaDebil(t.Tema, t.Unidad, t.Nota!.Value, t.Nivel)).ToList();

        var contenidos = new List<ContenidoDebil>();
        var fila = mapa?.Estudiantes.FirstOrDefault(f => f.EstudianteId == ficha.EstudianteId);
        if (mapa is not null && fila is not null)
        {
            var nombresDebiles = debiles.Select(d => d.Tema).ToHashSet();
            foreach (var columna in mapa.Temas.Where(t => nombresDebiles.Contains(t.Tema)))
                foreach (var c in columna.Contenidos)
                    if (fila.Contenidos.TryGetValue(c.ContenidoId, out var celda) && celda.Nota.HasValue && celda.Nivel != "Aprobado")
                        contenidos.Add(new ContenidoDebil(columna.Tema, c.Contenido, celda.Nota.Value));
        }

        var notas = ficha.Actividades
            .Where(a => a.Nota.HasValue && a.Estado is "Calificada" or "Cero")
            .Select(a => new NotaEvidencia(a.Nombre, a.Nota!.Value, a.Estado)).ToList();
        var sinEntrega = ficha.Actividades.Where(a => a.Estado == "SinEntrega" && a.Peso > 0).Select(a => a.Nombre).ToList();

        return new PerfilEstudiante(
            ficha.EstudianteId, ficha.Nivel, ficha.NotaPonderada, ficha.PesoEvaluado, ficha.Umbrales.Critico, ficha.Umbrales.Aprobado,
            debiles, contenidos.OrderBy(c => c.Nota).Take(MaxContenidos).ToList(), notas, sinEntrega,
            ficha.Alertas.Select(a => a.Evidencia).ToList(), ficha.Tendencia.Disponible ? ficha.Tendencia.Mensaje : null);
    }
}

/// <summary>HU-15, criterio 2: la evidencia (notas y temas) la arma el código, no el modelo.</summary>
public static class EvidenciaRecomendacion
{
    private static string F(decimal? v) => v is null ? "—" : CalculadoraIndicadores.Fmt(v.Value);

    public static string NivelTexto(string nivel) => nivel switch
    {
        "Critico" => "Crítico", "EnUmbral" => "En umbral", "Aprobado" => "Aprobado", _ => "Sin datos"
    };

    public static string Construir(PerfilEstudiante p)
    {
        var l = new List<string>
        {
            $"Nota ponderada {F(p.NotaPonderada)} / 10 ({NivelTexto(p.Nivel)}; crítico < {F(p.Critico)}, aprobado ≥ {F(p.Aprobado)}) " +
            $"con el {F(p.PesoEvaluado)} % del parcial evaluado."
        };
        if (p.Notas.Count > 0)
            l.Add("Notas: " + string.Join(" · ", p.Notas.Select(n => $"{n.Actividad} {F(n.Nota)}")));
        if (p.TemasDebiles.Count > 0)
            l.Add("Temas débiles: " + string.Join(" · ", p.TemasDebiles.Select(t => $"{t.Tema} {F(t.Nota)} ({NivelTexto(t.Nivel)})")));
        else
            l.Add("Temas débiles: ninguno con los datos disponibles.");
        if (p.ActividadesSinEntrega.Count > 0)
            l.Add("Sin entrega: " + string.Join(", ", p.ActividadesSinEntrega));
        if (p.Alertas.Count > 0)
            l.Add("Alertas: " + string.Join(" | ", p.Alertas.Take(3)));
        if (p.Tendencia is not null)
            l.Add("Tendencia: " + p.Tendencia);
        var texto = string.Join("\n", l);
        return texto.Length <= 2000 ? texto : texto[..2000];
    }
}

/// <summary>
/// Recomendaciones basadas en reglas, distintas por nivel de riesgo (HU-15, criterio 1: quien tiene 6 no recibe lo mismo
/// que quien tiene 2). Son el respaldo cuando el modelo no responde y la base de las falencias, que siempre se calculan.
/// </summary>
public static class PlantillasRecomendacion
{
    private static string F(decimal? v) => v is null ? "—" : CalculadoraIndicadores.Fmt(v.Value);

    public static ContenidoRecomendacionDto Generar(PerfilEstudiante p)
    {
        var falencias = Falencias(p);
        var actividades = Actividades(p);
        var repasar = ContenidosARepasar(p);
        var observacion = p.Nivel switch
        {
            "Critico" => "Prioridad alta: conviene contactar al estudiante esta semana.",
            "EnUmbral" => "Prioridad media: un refuerzo breve puede evitar que descienda.",
            _ => null
        };
        return new ContenidoRecomendacionDto(falencias, actividades, repasar, observacion);
    }

    public static IReadOnlyList<string> Falencias(PerfilEstudiante p)
    {
        var f = new List<string>();
        switch (p.Nivel)
        {
            case "Critico":
                f.Add($"Nota ponderada {F(p.NotaPonderada)}, por debajo del umbral crítico ({F(p.Critico)}).");
                break;
            case "EnUmbral":
                f.Add($"Nota ponderada {F(p.NotaPonderada)}: cerca del mínimo de aprobación ({F(p.Aprobado)}).");
                break;
            default:
                f.Add($"Nota ponderada {F(p.NotaPonderada)}: sin falencias críticas con los datos actuales.");
                break;
        }
        foreach (var t in p.TemasDebiles.Take(3))
            f.Add($"Dominio insuficiente en «{t.Tema}» ({F(t.Nota)}).");
        if (p.ActividadesSinEntrega.Count > 0)
            f.Add("Actividades sin entrega: " + string.Join(", ", p.ActividadesSinEntrega.Take(3)) + ".");
        return f;
    }

    public static IReadOnlyList<string> Actividades(PerfilEstudiante p)
    {
        var tema1 = p.TemasDebiles.FirstOrDefault()?.Tema;
        var tema2 = p.TemasDebiles.Skip(1).FirstOrDefault()?.Tema;
        var peor = p.Notas.OrderBy(n => n.Nota).FirstOrDefault();
        var a = new List<string>();

        switch (p.Nivel)
        {
            case "Critico":
                a.Add(tema1 is null ? "Tutoría individual esta semana para revisar los temas con menor nota."
                                    : $"Tutoría individual esta semana para revisar «{tema1}».");
                a.Add(tema1 is null ? "Resolver 5 ejercicios guiados de los contenidos con menor nota y entregarlos para retroalimentación."
                                    : $"Resolver 5 ejercicios guiados de «{tema1}» y entregarlos para retroalimentación.");
                if (tema2 is not null) a.Add($"Repaso dirigido de «{tema2}» con una práctica corta.");
                if (p.ActividadesSinEntrega.Count > 0)
                    a.Add("Entregar las actividades pendientes: " + string.Join(", ", p.ActividadesSinEntrega.Take(3)) + ".");
                else if (peor is not null)
                    a.Add($"Rehacer «{peor.Actividad}» aplicando las correcciones del docente.");
                a.Add($"Seguimiento semanal de avance hasta superar el umbral crítico ({F(p.Critico)}).");
                break;

            case "EnUmbral":
                a.Add(tema1 is null ? "Práctica focalizada de 2 o 3 ejercicios en los contenidos con menor nota."
                                    : $"Práctica focalizada de 2 o 3 ejercicios sobre «{tema1}».");
                if (peor is not null)
                    a.Add($"Revisar los errores de «{peor.Actividad}» ({F(peor.Nota)}) con el docente o un compañero.");
                a.Add($"Autoevaluación breve antes del siguiente parcial para asegurar el mínimo de aprobación ({F(p.Aprobado)}).");
                break;

            default:
                a.Add(tema1 is null ? "Actividad de profundización con un reto aplicado a los temas del parcial."
                                    : $"Actividad de profundización sobre «{tema1}», el tema con menor nota relativa.");
                a.Add("Puede apoyar como tutor par a compañeros con mayor dificultad.");
                break;
        }
        return a;
    }

    public static IReadOnlyList<string> ContenidosARepasar(PerfilEstudiante p)
    {
        if (p.ContenidosDebiles.Count > 0)
            return p.ContenidosDebiles.Select(c => $"«{c.Tema}» › {c.Contenido} ({F(c.Nota)})").ToList();
        return p.TemasDebiles.Select(t => $"Tema «{t.Tema}» ({t.Unidad})").ToList();
    }
}

/// <summary>Prompts por nivel de riesgo (T15.1) con salida estructurada en JSON.</summary>
public static class PromptsRecomendacion
{
    public const string CodigoEstudiante = "E01";

    public static string InstruccionNivel(string nivel) => nivel switch
    {
        "Critico" =>
            "El estudiante está en RIESGO CRÍTICO. Propón un plan de recuperación intensivo: de 3 a 4 actividades de refuerzo concretas y " +
            "secuenciadas, empezando por el tema más débil; incluye una tutoría y un seguimiento con fecha cercana.",
        "EnUmbral" =>
            "El estudiante está EN UMBRAL, cerca del mínimo. Propón un refuerzo focalizado y breve: 2 o 3 actividades puntuales en los " +
            "temas límite y una autoevaluación. No lo trates como riesgo crítico.",
        _ =>
            "El estudiante APRUEBA. Propón 1 o 2 actividades de profundización o de apoyo entre pares. No lo trates como estudiante en riesgo."
    };

    public static IReadOnlyList<LlmMensaje> Construir(PerfilEstudiante p, string evidencia)
    {
        var sistema =
            "Eres un asistente pedagógico para docentes universitarios. Responde en español.\n" +
            "Reglas obligatorias:\n" +
            $"1. Habla del estudiante solo como «{CodigoEstudiante}». No inventes nombres, notas, temas ni actividades que no estén en los datos.\n" +
            "2. Basa cada actividad en los temas y notas de la EVIDENCIA.\n" +
            "3. Son sugerencias orientativas que el docente revisará y aprobará.\n" +
            InstruccionNivel(p.Nivel) + "\n" +
            "Devuelve SOLO un objeto JSON con esta forma exacta:\n" +
            "{\"actividadesRefuerzo\":[\"...\"],\"observacion\":\"...\"}\n" +
            "- actividadesRefuerzo: acciones concretas y breves (una frase cada una).\n" +
            "- observacion: una frase con la prioridad o el enfoque pedagógico.";
        var usuario = $"ESTUDIANTE: {CodigoEstudiante}\nNIVEL: {EvidenciaRecomendacion.NivelTexto(p.Nivel)}\nEVIDENCIA:\n{evidencia}";
        return new[] { new LlmMensaje(LlmMensaje.Sistema, sistema), new LlmMensaje(LlmMensaje.Usuario, usuario) };
    }

    private static readonly Regex Codigo = new(@"\bE\d{1,4}\b", RegexOptions.Compiled);

    /// <summary>
    /// Lee la salida del modelo. Descarta líneas que citen otro código de estudiante y reemplaza «E01» por «el estudiante».
    /// Devuelve null si no queda ninguna actividad utilizable (entonces se usa la plantilla).
    /// </summary>
    public static (IReadOnlyList<string> Actividades, string? Observacion)? Parsear(string? texto)
    {
        var obj = SalidaModelo.ExtraerObjeto(texto);
        if (obj is null) return null;

        string? Depurar(string linea)
        {
            if (Codigo.Matches(linea).Any(m => m.Value != CodigoEstudiante)) return null;
            return Regex.Replace(linea, $@"\b{CodigoEstudiante}\b", "el estudiante");
        }

        var actividades = SalidaModelo.Lista(obj.Value, "actividadesRefuerzo").Select(Depurar).Where(x => x is not null).Select(x => x!).ToList();
        if (actividades.Count == 0) return null;
        var observacion = SalidaModelo.Lista(obj.Value, "observacion").Select(Depurar).FirstOrDefault(x => x is not null);
        return (actividades, observacion);
    }
}
