using EduMonitor.Analytics.Api.Services;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>
/// T10.2 / T11.4 / T12.1 — Casos calculados a mano con los datos de prueba de Programación Web:
/// Evaluación 1 (40 %), APE 1 y APE 2 (15 % c/u) y Trabajo autónomo 1 a 4 (7,5 % c/u).
/// </summary>
public class CalculadoraIndicadoresTests
{
    private static readonly IReadOnlyList<ActividadCalc> Actividades = new List<ActividadCalc>
    {
        new(1, "Evaluación 1", "Evaluacion", 40m, 1),
        new(2, "APE 1", "APE", 15m, 2),
        new(3, "APE 2", "APE", 15m, 3),
        new(4, "Trabajo autónomo 1", "TrabajoAutonomo", 7.5m, 4),
        new(5, "Trabajo autónomo 2", "TrabajoAutonomo", 7.5m, 5),
        new(6, "Trabajo autónomo 3", "TrabajoAutonomo", 7.5m, 6),
        new(7, "Trabajo autónomo 4", "TrabajoAutonomo", 7.5m, 7),
    };

    private static Dictionary<int, NotaCalc> Notas(params (decimal? nota, EstadoNota estado)[] valores)
    {
        var d = new Dictionary<int, NotaCalc>();
        for (var i = 0; i < valores.Length; i++) d[i + 1] = new NotaCalc(i + 1, valores[i].nota, valores[i].estado);
        return d;
    }

    private static (decimal?, EstadoNota) C(decimal n) => (n, EstadoNota.Calificada);
    private static readonly (decimal?, EstadoNota) Pendiente = (null, EstadoNota.PendienteDeCalificar);
    private static readonly (decimal?, EstadoNota) SinEntrega = (null, EstadoNota.SinEntrega);
    private static readonly (decimal?, EstadoNota) Cero = (0m, EstadoNota.Cero);

    // ---------------------------------------------------------------- nota ponderada 40/30/30
    [Fact]
    public void NotaPonderada_CasoCompleto_Lucia()
    {
        // 9×40 + 8,5×15 + 9×15 + (9,5+9+8,5+9)×7,5 = 892,5  →  /100 = 8,925 → 8,93
        var r = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades, Notas(C(9), C(8.5m), C(9), C(9.5m), C(9), C(8.5m), C(9)));
        Assert.Equal(8.93m, r.NotaPonderada);
        Assert.Equal(8.93m, r.NotaAcumulada);
        Assert.Equal(100m, r.PesoEvaluado);
        Assert.Equal(7, r.Evaluadas);
    }

    [Fact]
    public void NotaPonderada_Andres_6_39()
    {
        // 6,5×40 + 7×15 + 6×15 + (6,5+6+7+5)×7,5 = 638,75 → 6,3875 → 6,39
        var r = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades, Notas(C(6.5m), C(7), C(6), C(6.5m), C(6), C(7), C(5)));
        Assert.Equal(6.39m, r.NotaPonderada);
    }

    [Fact]
    public void NotaPonderada_ConActividadesSinCalificar_InformaPesoEvaluado()
    {
        // Joel: E1 2,5 · APE1 4 · APE2 pendiente · TA1 6 · TA2 cero · TA3 sin entrega · TA4 3
        // evaluado = 40+15+7,5+7,5+7,5 = 77,5 %  ·  suma = 100+60+45+0+22,5 = 227,5
        var r = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades,
            Notas(C(2.5m), C(4), Pendiente, C(6), Cero, SinEntrega, C(3)));
        Assert.Equal(77.5m, r.PesoEvaluado);
        Assert.Equal(2.94m, r.NotaPonderada);    // 227,5 / 77,5 = 2,935…
        Assert.Equal(2.28m, r.NotaAcumulada);    // 227,5 / 100 = 2,275
        Assert.Equal(5, r.Evaluadas);
        Assert.Equal(1, r.SinEntrega);
        Assert.Equal(1, r.Pendientes);
    }

    [Fact]
    public void NotaCero_SeCuentaComoEvaluada_PeroSinEntregaNo()
    {
        var conCero = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades, Notas(C(10), Cero));
        var conSinEntrega = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades, Notas(C(10), SinEntrega));
        Assert.Equal(7.27m, conCero.NotaPonderada);        // (10×40 + 0×15) / 55 = 7,27: el cero SÍ castiga
        Assert.Equal(10m, conSinEntrega.NotaPonderada);    // sin entrega no se evalúa todavía: 400 / 40 = 10 (RN-08)
    }

    [Fact]
    public void SinNadaEvaluado_NoHayNota()
    {
        var r = CalculadoraIndicadores.CalcularNotaEstudiante(Actividades, Notas(SinEntrega, Pendiente));
        Assert.Null(r.NotaPonderada);
        Assert.Equal(0m, r.PesoEvaluado);
    }

    // ---------------------------------------------------------------- niveles de riesgo (RN-04)
    [Theory]
    [InlineData(4.6, 1)]   // HU-11, criterio 1: con umbrales 5 y 7, 4,6 es Crítico
    [InlineData(4.99, 1)]
    [InlineData(5.0, 2)]
    [InlineData(6.99, 2)]
    [InlineData(7.0, 3)]
    [InlineData(10.0, 3)]
    public void Nivel_ConUmbralesPorDefecto(double nota, int esperado) =>
        Assert.Equal((NivelRiesgo)esperado, CalculadoraIndicadores.NivelDe((decimal)nota, 5m, 7m));

    [Fact]
    public void Nivel_SinNota_EsSinDatos() =>
        Assert.Equal(NivelRiesgo.SinDatos, CalculadoraIndicadores.NivelDe(null, 5m, 7m));

    [Fact]
    public void Nivel_RespetaUmbralesConfigurados()
    {
        // HU-11, criterio 2: al cambiar el umbral se recalcula la clasificación
        Assert.Equal(NivelRiesgo.EnUmbral, CalculadoraIndicadores.NivelDe(4.6m, 4m, 7m));
        Assert.Equal(NivelRiesgo.Aprobado, CalculadoraIndicadores.NivelDe(6.5m, 4m, 6m));
    }

    // ---------------------------------------------------------------- estadística
    [Fact]
    public void Promedio_YDesviacionMuestral()
    {
        Assert.Equal(7m, CalculadoraIndicadores.Promedio(new List<decimal> { 6, 7, 8 }));
        // [2,4,4,4,5,5,7,9]: media 5, suma de cuadrados 32, 32/7 = 4,571 → raíz 2,138
        Assert.Equal(2.14m, CalculadoraIndicadores.DesviacionMuestral(new List<decimal> { 2, 4, 4, 4, 5, 5, 7, 9 }));
        Assert.Equal(0m, CalculadoraIndicadores.DesviacionMuestral(new List<decimal> { 7 }));
        Assert.Null(CalculadoraIndicadores.Promedio(new List<decimal>()));
    }

    [Fact]
    public void Distribucion_UsaLosRangosDelMockup_YIncluyeElDiez()
    {
        var notas = new List<decimal> { 1, 2, 3.99m, 4, 4.99m, 5, 6.99m, 7, 8.49m, 8.5m, 10 };
        var d = CalculadoraIndicadores.Distribucion(notas);
        Assert.Equal(6, d.Count);
        Assert.Equal(1, d[0].Cantidad);   // 0–2
        Assert.Equal(2, d[1].Cantidad);   // 2–4
        Assert.Equal(2, d[2].Cantidad);   // 4–5
        Assert.Equal(2, d[3].Cantidad);   // 5–7
        Assert.Equal(2, d[4].Cantidad);   // 7–8,5
        Assert.Equal(2, d[5].Cantidad);   // 8,5–10
    }

    // ---------------------------------------------------------------- alertas con evidencia
    [Fact]
    public void Alertas_Joel_NotasBajas_YSinEntrega_ConEvidencia()
    {
        var notas = Notas(C(2.5m), C(4), Pendiente, C(6), Cero, SinEntrega, C(3));
        var alertas = CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 5m);

        var bajas = alertas.Where(a => a.Tipo == "NotaBaja").ToList();
        Assert.Equal(4, bajas.Count);   // Evaluación 1 (2,5), APE 1 (4), TA2 (0) y TA4 (3)
        Assert.Contains(bajas, a => a.Evidencia == "Nota 2,5 en Evaluación 1 (umbral crítico < 5)");

        var sin = Assert.Single(alertas.Where(a => a.Tipo == "SinEntrega"));
        Assert.Equal(1m, sin.Valor);    // solo TA3: APE 2 está pendiente de calificar, no sin entrega
        Assert.Contains("Trabajo autónomo 3", sin.Evidencia);
        Assert.DoesNotContain("APE 2", sin.Evidencia);
    }

    [Fact]
    public void Alertas_DetectaDescensoEnLasUltimasTresNotas()
    {
        // Diego: … TA2 5 → TA3 4 → TA4 3
        var notas = Notas(C(4.5m), C(6), C(5), C(6), C(5), C(4), C(3));
        var alertas = CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 5m);
        var descenso = Assert.Single(alertas.Where(a => a.Tipo == "Descenso"));
        Assert.Contains("Trabajo autónomo 2 5", descenso.Evidencia);
        Assert.Contains("Trabajo autónomo 4 3", descenso.Evidencia);
    }

    [Fact]
    public void Alertas_NoHayDescenso_SiLaCaidaEsMenorAUnPunto()
    {
        var notas = Notas(C(8), C(8), C(8), C(8), C(8), C(7.8m), C(7.6m));
        var alertas = CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 5m);
        Assert.DoesNotContain(alertas, a => a.Tipo == "Descenso");
    }

    [Fact]
    public void Alertas_EstudianteSano_NoGeneraNinguna()
    {
        var notas = Notas(C(9), C(8.5m), C(9), C(9.5m), C(9), C(8.5m), C(9));
        Assert.Empty(CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 5m));
    }

    [Fact]
    public void Alertas_SeAdaptanAlUmbralConfigurado()
    {
        var notas = Notas(C(4.5m), C(8), C(8), C(8), C(8), C(8), C(8));
        Assert.Single(CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 5m));
        Assert.Empty(CalculadoraIndicadores.GenerarAlertas(Actividades, notas, 4m));
    }

    // ---------------------------------------------------------------- desempeño por tema / contenido
    [Fact]
    public void Desempeno_PromedioPonderadoPorPeso_IgnoraLoNoEvaluado()
    {
        var acts = Actividades.ToDictionary(a => a.Id);
        // Joel: APE1 = 4 (15 %), APE2 pendiente, TA1 = 6 (7,5 %)
        var notas = Notas(C(2.5m), C(4), Pendiente, C(6), Cero, SinEntrega, C(3));
        // (4×15 + 6×7,5) / 22,5 = 105 / 22,5 = 4,666… → 4,67
        Assert.Equal(4.67m, CalculadoraIndicadores.DesempenoGrupo(new[] { 2, 3, 4 }, acts, notas));
    }

    [Fact]
    public void Desempeno_SinActividadesEvaluadas_EsNulo()
    {
        var acts = Actividades.ToDictionary(a => a.Id);
        var notas = Notas(C(2.5m), C(4), Pendiente, C(6), Cero, SinEntrega, C(3));
        Assert.Null(CalculadoraIndicadores.DesempenoGrupo(new[] { 3, 6 }, acts, notas));   // pendiente + sin entrega
        Assert.Null(CalculadoraIndicadores.DesempenoGrupo(Array.Empty<int>(), acts, notas)); // contenido sin actividades mapeadas
    }
}
