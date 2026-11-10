using EduMonitor.Academic.Api.Domain;
using EduMonitor.Academic.Api.Services;
using Xunit;

namespace EduMonitor.Academic.Tests;

/// <summary>HU-07, criterio 2 · RN-08: nota cero, sin entrega y pendiente de calificar son estados distintos.</summary>
public class GradeStateClassifierTests
{
    [Fact]
    public void SinNota_YSinEntrega_EsSinEntrega() =>
        Assert.Equal(EstadoCalificacion.SinEntrega, GradeStateClassifier.Clasificar(null, entregado: false));

    [Fact]
    public void SinNota_PeroEntregada_EsPendienteDeCalificar() =>
        Assert.Equal(EstadoCalificacion.PendienteDeCalificar, GradeStateClassifier.Clasificar(null, entregado: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotaCero_EsCero_ConOSinEntrega(bool entregado) =>
        Assert.Equal(EstadoCalificacion.Cero, GradeStateClassifier.Clasificar(0m, entregado));

    [Fact]
    public void ConNota_EsCalificada() =>
        Assert.Equal(EstadoCalificacion.Calificada, GradeStateClassifier.Clasificar(7.5m, entregado: true));

    [Fact]
    public void NormalizarEscala_LlevaA10_YLimita()
    {
        Assert.Equal(8.5m, GradeStateClassifier.NormalizarEscala(85m, 100m));
        Assert.Equal(8m, GradeStateClassifier.NormalizarEscala(8m, 10m));
        Assert.Equal(8.33m, GradeStateClassifier.NormalizarEscala(25m, 30m));
        Assert.Equal(10m, GradeStateClassifier.NormalizarEscala(12m, 10m));
        Assert.Equal(10m, GradeStateClassifier.NormalizarEscala(50m, 20m));
        Assert.Null(GradeStateClassifier.NormalizarEscala(null, 10m));
    }

    [Fact]
    public void ParseNotaFormateada_AceptaComaDecimal_YRechazaTextos()
    {
        Assert.Equal(8.5m, GradeStateClassifier.ParseNotaFormateada("8,50"));
        Assert.Equal(7m, GradeStateClassifier.ParseNotaFormateada(" 7 "));
        Assert.Null(GradeStateClassifier.ParseNotaFormateada("-"));
        Assert.Null(GradeStateClassifier.ParseNotaFormateada("85,00 %"));
        Assert.Null(GradeStateClassifier.ParseNotaFormateada(""));
        Assert.Null(GradeStateClassifier.ParseNotaFormateada(null));
    }

    [Fact]
    public void NombreVisible_UsaElCorreoSiNoHayNombre()
    {
        Assert.Equal("Joel Herrera", GradeStateClassifier.NombreVisible("Joel", "Herrera", "joel@uta.edu.ec"));
        Assert.Equal("joel.herrera", GradeStateClassifier.NombreVisible("", "", "joel.herrera@uta.edu.ec"));
        Assert.Equal("Estudiante", GradeStateClassifier.NombreVisible(null, null, null));
    }
}
