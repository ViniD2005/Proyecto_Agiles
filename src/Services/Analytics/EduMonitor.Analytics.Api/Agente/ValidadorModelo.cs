using System.Text.RegularExpressions;

namespace EduMonitor.Analytics.Api.Agente;

/// <summary>
/// RN-09 / HU-13 criterio 3: el modelo solo explica. Descarta lo que afirma sin respaldo en los hechos calculados:
/// cifras que el sistema no calculó, una nota atribuida a otro estudiante, «sin datos» cuando todos tienen notas
/// y tendencias que el sistema no calculó.
/// </summary>
public static class ValidadorModelo
{
    private static readonly Regex Decimal = new(@"\d+[.,]\d+", RegexOptions.Compiled);
    private static readonly Regex CodigoCifra = new(@"\b(E\d{1,4})\b[^\dE.;]{0,25}?(\d+[.,]\d+)", RegexOptions.Compiled);
    private static readonly Regex SinDatos = new(@"sin datos|no tien\w* datos|no hay datos del estudiante|sin calificaci", RegexOptions.Compiled);
    private static readonly Regex Relleno = new(@"no hay datos adicionales|sin datos adicionales|no hay informaci\w+ adicional", RegexOptions.Compiled);
    private static readonly Regex Tendencia = new(@"tendencia|descendente|ascendente|descenso|empeor|mejorando", RegexOptions.Compiled);

    private static string Num(string s)
    {
        var n = s.Replace(',', '.');
        if (n.Contains('.')) n = n.TrimEnd('0').TrimEnd('.');
        return n;
    }

    /// <param name="evidenciaModelo">Hechos seleccionados, con los códigos E01… que ve el modelo.</param>
    /// <param name="sinDatosEnCurso">Cuántos estudiantes no tienen notas evaluadas (0 si todos tienen).</param>
    /// <param name="esIncertidumbre">Las líneas de incertidumbre pueden hablar de tendencias que faltan; las demás no.</param>
    public static bool EstaRespaldada(string linea, string evidenciaModelo, int sinDatosEnCurso, bool esIncertidumbre)
    {
        var l = Anonimizador.Normalizar(linea);
        var ev = Anonimizador.Normalizar(evidenciaModelo);

        // 1) toda cifra decimal debe haber sido calculada por el sistema
        var cifrasEvidencia = Decimal.Matches(evidenciaModelo).Select(m => Num(m.Value)).ToHashSet();
        foreach (Match m in Decimal.Matches(linea))
            if (!cifrasEvidencia.Contains(Num(m.Value))) return false;

        // 2) la nota de un código debe ser la de ese código, no la de otro
        var porCodigo = CodigoCifra.Matches(evidenciaModelo)
            .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.Select(m => Num(m.Groups[2].Value)).ToHashSet());
        foreach (Match m in CodigoCifra.Matches(linea))
            if (porCodigo.TryGetValue(m.Groups[1].Value, out var validas) && !validas.Contains(Num(m.Groups[2].Value))) return false;

        // 3) «sin datos» solo si el sistema lo calculó
        if (sinDatosEnCurso == 0 && SinDatos.IsMatch(l)) return false;

        // 4) relleno sin contenido
        if (esIncertidumbre && Relleno.IsMatch(l)) return false;

        // 5) tendencias: solo si aparecen en los hechos
        if (!esIncertidumbre && Tendencia.IsMatch(l) && !Tendencia.IsMatch(ev)) return false;

        return true;
    }
}
