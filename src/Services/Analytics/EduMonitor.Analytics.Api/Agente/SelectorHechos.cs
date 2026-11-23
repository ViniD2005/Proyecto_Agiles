using EduMonitor.Analytics.Api.Agente.Herramientas;

namespace EduMonitor.Analytics.Api.Agente;

/// <summary>
/// Elige, entre lo que devolvieron las herramientas, solo lo que responde a la pregunta (Enfoque). Así cada respuesta
/// es específica y el modelo recibe menos contexto (más rápido y menos propenso a mezclar temas).
/// </summary>
public static class SelectorHechos
{
    public sealed record Seleccion(IReadOnlyList<string> Hechos, IReadOnlyList<string> Datos);

    private static bool Contiene(string linea, params string[] claves) =>
        claves.Any(c => Anonimizador.Normalizar(linea).Contains(c));

    public static Seleccion Elegir(Enfoque enfoque, IReadOnlyList<(string Nombre, ResultadoHerramienta Resultado)> exitosas)
    {
        var hechos = new List<string>();
        var datos = new List<string>();

        void Tomar(string herramienta, Func<string, bool>? filtro = null, int? primeras = null, bool conDatos = true)
        {
            foreach (var x in exitosas.Where(e => e.Nombre == herramienta))
            {
                var lineas = x.Resultado.Hechos.AsEnumerable();
                if (filtro is not null) lineas = lineas.Where(filtro);
                if (primeras is not null) lineas = lineas.Take(primeras.Value);
                hechos.AddRange(lineas);
                if (conDatos && x.Resultado.DatosJson is not null) datos.Add($"[{x.Nombre}] {x.Resultado.DatosJson}");
            }
        }

        if (enfoque.HasFlag(Enfoque.Estudiante))
        {
            // la pregunta es sobre una persona: su ficha y, como referencia, solo la línea del promedio del curso
            Tomar("obtener_ficha_estudiante");
            Tomar("obtener_calificaciones", primeras: 1, conDatos: false);
            return Distintos(hechos, datos);
        }

        if (enfoque.HasFlag(Enfoque.Resumen))
        {
            Tomar("listar_cursos_autorizados", conDatos: false);
            Tomar("obtener_calificaciones");
            Tomar("identificar_alertas");
            Tomar("generar_resumen");
            Tomar("obtener_desempeno_temas");
            return Distintos(hechos, datos);
        }

        if (enfoque.HasFlag(Enfoque.Riesgo))
        {
            Tomar("identificar_alertas");
            Tomar("obtener_calificaciones", primeras: 3);
            Tomar("obtener_desempeno_temas");
            Tomar("generar_resumen", l => Contiene(l, "grupos diferenciados", "temas criticos"), conDatos: false);
        }
        if (enfoque.HasFlag(Enfoque.Alertas))
        {
            Tomar("identificar_alertas");
            Tomar("generar_resumen", l => Contiene(l, "sin entrega", "cero", "pendiente"));
        }
        if (enfoque.HasFlag(Enfoque.Entregas) && !enfoque.HasFlag(Enfoque.Alertas))
        {
            // solo lo que trata de entregas: sin datos JSON de notas, para que el modelo no hable de otra cosa
            Tomar("identificar_alertas", l => Contiene(l, "sin entrega", "cero"), conDatos: false);
            Tomar("generar_resumen", l => Contiene(l, "sin entrega", "cero", "pendiente"), conDatos: false);
        }
        if (enfoque.HasFlag(Enfoque.Temas))
        {
            Tomar("obtener_desempeno_temas");
            Tomar("generar_resumen", l => Contiene(l, "tema", "estructura", "mape"));
        }
        if (enfoque.HasFlag(Enfoque.Calificaciones))
        {
            Tomar("obtener_calificaciones");
            Tomar("generar_resumen", l => Contiene(l, "grupos diferenciados", "peso evaluado"), conDatos: false);
        }
        return Distintos(hechos, datos);
    }

    private static Seleccion Distintos(List<string> hechos, List<string> datos) =>
        new(hechos.Distinct().ToList(), datos.Distinct().ToList());
}
