using EduMonitor.Analytics.Api.Services;
using Xunit;

namespace EduMonitor.Analytics.Tests;

/// <summary>T19.x (HU-19, opcional) — Tendencia por regresión lineal y alerta temprana. Umbrales por defecto 5 y 7.</summary>
public class TendenciaTests
{
    private const decimal Critico = 5m, Aprobado = 7m;

    [Fact]
    public void ConMenosDeTresNotasNoHayProyeccion()
    {
        var r = CalculadoraTendencia.Calcular(new List<decimal> { 8m, 9m }, Critico, Aprobado);
        Assert.False(r.Disponible);
        Assert.Null(r.Proyeccion);
        Assert.False(r.AlertaTemprana);
        Assert.Contains("insuficientes", r.Mensaje);
    }

    [Fact]
    public void NotasQueBajanProducenTendenciaDescendenteYAlertaTemprana()
    {
        var r = CalculadoraTendencia.Calcular(new List<decimal> { 9m, 8m, 7m }, Critico, Aprobado);
        Assert.True(r.Disponible);
        Assert.Equal("Descendente", r.Direccion);
        Assert.Equal(-1m, r.Pendiente);
        Assert.Equal(6m, r.Proyeccion);
        Assert.True(r.AlertaTemprana);
        Assert.NotNull(r.MensajeAlerta);
    }

    [Fact]
    public void NotasQueSuben_NoGeneranAlerta()
    {
        var r = CalculadoraTendencia.Calcular(new List<decimal> { 6m, 7m, 8m }, Critico, Aprobado);
        Assert.Equal("Ascendente", r.Direccion);
        Assert.Equal(9m, r.Proyeccion);
        Assert.False(r.AlertaTemprana);
        Assert.Null(r.MensajeAlerta);
    }

    [Fact]
    public void NotasConstantesSonEstables()
    {
        var r = CalculadoraTendencia.Calcular(new List<decimal> { 8m, 8m, 8m }, Critico, Aprobado);
        Assert.Equal("Estable", r.Direccion);
        Assert.Equal(8m, r.Proyeccion);
        Assert.False(r.AlertaTemprana);
    }

    [Fact]
    public void LaProyeccionNuncaSaleDeLaEscalaDeCeroADiez()
    {
        var baja = CalculadoraTendencia.Calcular(new List<decimal> { 4m, 2m, 0m }, Critico, Aprobado);
        var sube = CalculadoraTendencia.Calcular(new List<decimal> { 6m, 8m, 10m }, Critico, Aprobado);
        Assert.Equal(0m, baja.Proyeccion);
        Assert.Equal(10m, sube.Proyeccion);
        Assert.True(baja.AlertaTemprana);
        Assert.All(sube.Linea, v => Assert.InRange(v, 0m, 10m));
    }

    [Fact]
    public void LaLineaIncluyeUnPuntoMasQueLasNotas()
    {
        var r = CalculadoraTendencia.Calcular(new List<decimal> { 9m, 8m, 7m, 6m }, Critico, Aprobado);
        Assert.Equal(5, r.Linea.Count);
    }

    [Fact]
    public void ElAvisoDejaClaroQueEsUnaEstimacion()
    {
        Assert.Contains("estimación", CalculadoraTendencia.Aviso);
    }
}
