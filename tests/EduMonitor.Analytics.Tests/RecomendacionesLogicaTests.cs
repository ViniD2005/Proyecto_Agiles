using EduMonitor.Analytics.Api.Recomendaciones;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>T15.x — Recomendaciones diferenciadas por nivel de riesgo, evidencia por código y lectura segura de la salida del modelo.</summary>
public class RecomendacionesLogicaTests
{
    private static PerfilEstudiante Perfil(string nivel, decimal? nota, bool conTemas = true, string[]? sinEntrega = null) => new(
        10, nivel, nota, 100m, 5m, 7m,
        conTemas
            ? new List<TemaDebil> { new("HTML y CSS", "Unidad 1", 3.5m, "Critico"), new("JavaScript", "Unidad 1", 5.5m, "EnUmbral") }
            : new List<TemaDebil>(),
        new List<ContenidoDebil> { new("HTML y CSS", "Etiquetas semánticas", 3m) },
        new List<NotaEvidencia> { new("Evaluación 1", 3.5m, "Calificada"), new("APE 1", 6m, "Calificada") },
        sinEntrega ?? Array.Empty<string>(), new List<string>(), null);

    [Fact]
    public void LasRecomendacionesDifierenSegunElNivel()
    {
        var critico = PlantillasRecomendacion.Generar(Perfil("Critico", 4.2m));
        var umbral = PlantillasRecomendacion.Generar(Perfil("EnUmbral", 6.1m));
        var aprobado = PlantillasRecomendacion.Generar(Perfil("Aprobado", 8.8m, conTemas: false));

        Assert.NotEqual(critico.ActividadesRefuerzo, umbral.ActividadesRefuerzo);
        Assert.NotEqual(umbral.ActividadesRefuerzo, aprobado.ActividadesRefuerzo);
        Assert.Contains(critico.ActividadesRefuerzo, a => a.Contains("Tutoría individual"));
        Assert.DoesNotContain(umbral.ActividadesRefuerzo, a => a.Contains("Tutoría individual"));
        Assert.Contains(aprobado.ActividadesRefuerzo, a => a.Contains("profundización"));
        Assert.True(critico.ActividadesRefuerzo.Count > umbral.ActividadesRefuerzo.Count);
    }

    [Fact]
    public void ElEstudianteCriticoRecibeObservacionDePrioridadAlta()
    {
        Assert.Contains("alta", PlantillasRecomendacion.Generar(Perfil("Critico", 4.2m)).Observacion);
        Assert.Null(PlantillasRecomendacion.Generar(Perfil("Aprobado", 9m, conTemas: false)).Observacion);
    }

    [Fact]
    public void LasFalenciasSeCitanConLasNotasReales()
    {
        var f = PlantillasRecomendacion.Falencias(Perfil("Critico", 4.2m, sinEntrega: new[] { "Trabajo autónomo 1" }));
        Assert.Contains(f, x => x.Contains("4,2") || x.Contains("4.2"));
        Assert.Contains(f, x => x.Contains("HTML y CSS"));
        Assert.Contains(f, x => x.Contains("Trabajo autónomo 1"));
    }

    [Fact]
    public void LasActividadesSinEntregaSePidenAlEstudianteCritico()
    {
        var a = PlantillasRecomendacion.Actividades(Perfil("Critico", 4.2m, sinEntrega: new[] { "Trabajo autónomo 1" }));
        Assert.Contains(a, x => x.Contains("Entregar las actividades pendientes"));
    }

    [Fact]
    public void ElContenidoARepasarPrefiereElDetalleDelMapaDeCalor()
    {
        var repasar = PlantillasRecomendacion.ContenidosARepasar(Perfil("Critico", 4.2m));
        Assert.Single(repasar);
        Assert.Contains("Etiquetas semánticas", repasar[0]);
    }

    [Fact]
    public void LaEvidenciaLaArmaElCodigoConLosDatosDelPerfil()
    {
        var e = EvidenciaRecomendacion.Construir(Perfil("Critico", 4.2m));
        Assert.Contains("Evaluación 1", e);
        Assert.Contains("HTML y CSS", e);
    }

    // ---------------------------------------------------------------- salida del modelo
    [Fact]
    public void SeLeeElJsonDelModelo_YSeReemplazaElCodigoDelEstudiante()
    {
        var r = PromptsRecomendacion.Parsear("{\"actividadesRefuerzo\":[\"Practicar con E01 tres ejercicios\",\"Revisar apuntes\"],\"observacion\":\"Seguimiento\"}");
        Assert.NotNull(r);
        Assert.Equal(2, r!.Value.Actividades.Count);
        Assert.DoesNotContain("E01", r.Value.Actividades[0]);
        Assert.Contains("el estudiante", r.Value.Actividades[0]);
    }

    [Fact]
    public void SeDescartaLaLineaQueCitaAOtroEstudiante()
    {
        var r = PromptsRecomendacion.Parsear("{\"actividadesRefuerzo\":[\"Pedir ayuda a E02\",\"Repasar HTML\"]}");
        Assert.NotNull(r);
        Assert.Equal(new[] { "Repasar HTML" }, r!.Value.Actividades);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"actividadesRefuerzo\":[]}")]
    [InlineData("{\"actividadesRefuerzo\":[\"Hablar con E05\"]}")]
    public void SiLaSalidaNoSirveSeDevuelveNulo_YSeUsaLaPlantilla(string? texto)
    {
        Assert.Null(PromptsRecomendacion.Parsear(texto));
    }

    [Fact]
    public void ElPromptDelNivelCriticoEsDistintoAlDelAprobado()
    {
        Assert.NotEqual(PromptsRecomendacion.InstruccionNivel("Critico"), PromptsRecomendacion.InstruccionNivel("Aprobado"));
    }
}
