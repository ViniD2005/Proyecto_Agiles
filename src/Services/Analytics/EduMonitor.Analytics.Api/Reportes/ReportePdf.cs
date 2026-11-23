using EduMonitor.Analytics.Api.Contracts;
using EduMonitor.Analytics.Api.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduMonitor.Analytics.Api.Reportes;

/// <summary>
/// Genera el PDF del reporte (T16.1) con QuestPDF (licencia Community). Contiene período, métricas, hallazgos,
/// recomendaciones, fecha de actualización y la nota de que las recomendaciones son orientativas.
/// </summary>
public sealed class QuestPdfReporte : IGeneradorPdfReporte
{
    private static string F(decimal? v) => v is null ? "—" : CalculadoraIndicadores.Fmt(v.Value);
    // El contenedor corre en UTC; el reporte se muestra en hora de Ecuador (UTC-5, sin horario de verano), igual que la pantalla.
    private static readonly TimeZoneInfo Zona = BuscarZona();

    private static TimeZoneInfo BuscarZona()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil"); }
        catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("Ecuador", TimeSpan.FromHours(-5), "Ecuador", "Ecuador"); }
    }

    private static string Fecha(DateTime? utc) => utc is null
        ? "—"
        : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Zona).ToString("dd/MM/yyyy HH:mm");

    static QuestPdfReporte() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Generar(ReporteDto r)
    {
        var azul = Colors.Blue.Darken3;
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(h =>
                {
                    h.Item().Text("Reporte inteligente del curso").FontSize(18).SemiBold().FontColor(azul);
                    h.Item().Text($"{r.Materia} · {r.Periodo}").FontSize(11);
                    h.Item().Text($"Generado: {Fecha(r.GeneradoUtc)} · Datos actualizados: {Fecha(r.DatosActualizadosUtc)} (hora de Ecuador) · Fuente: {r.Fuente}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Row(fila =>
                    {
                        Metrica(fila, "Promedio", F(r.Curso.Promedio));
                        Metrica(fila, "Desv. estándar", F(r.Curso.Desviacion));
                        Metrica(fila, "Críticos", r.Curso.Criticos.ToString());
                        Metrica(fila, "En umbral", r.Curso.EnUmbral.ToString());
                        Metrica(fila, "Aprobados", r.Curso.Aprobados.ToString());
                        Metrica(fila, "Estudiantes", r.Curso.Estudiantes.ToString());
                    });
                    col.Item().Text($"Escala 0–10 · umbrales: crítico < {F(r.Umbrales.Critico)}, aprobado ≥ {F(r.Umbrales.Aprobado)}" +
                                    $" · peso evaluado promedio {F(r.Curso.PesoEvaluadoPromedio)} %")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);

                    Seccion(col, "Hallazgos", r.Hallazgos);

                    if (r.Temas.Count > 0)
                    {
                        col.Item().Text("Desempeño por tema (de menor a mayor promedio)").SemiBold().FontColor(azul);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(3); c.RelativeColumn(1.5f); c.RelativeColumn(2); });
                            t.Header(h =>
                            {
                                Celda(h.Cell(), "Tema", true); Celda(h.Cell(), "Unidad", true);
                                Celda(h.Cell(), "Promedio", true); Celda(h.Cell(), "Críticos", true);
                            });
                            foreach (var x in r.Temas)
                            {
                                Celda(t.Cell(), x.Tema); Celda(t.Cell(), x.Unidad);
                                Celda(t.Cell(), $"{F(x.Promedio)} ({EtiquetaNivel(x.Nivel)})"); Celda(t.Cell(), x.EstudiantesCriticos.ToString());
                            }
                        });
                    }

                    Seccion(col, "Recomendaciones grupales", r.Recomendaciones);

                    if (!string.IsNullOrWhiteSpace(r.AnalisisIa))
                    {
                        col.Item().Text("Análisis redactado con apoyo de IA").SemiBold().FontColor(azul);
                        col.Item().Text(r.AnalisisIa).Italic();
                    }

                    col.Item().PaddingTop(6).Text(r.Aviso).FontSize(9).Italic().FontColor(Colors.Grey.Darken2);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("EduMonitor AI · Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static string EtiquetaNivel(string n) => n switch { "Critico" => "crítico", "EnUmbral" => "en umbral", "Aprobado" => "aprobado", _ => "sin datos" };

    private static void Metrica(RowDescriptor fila, string titulo, string valor) =>
        fila.RelativeItem().Column(c =>
        {
            c.Item().Text(valor).FontSize(16).SemiBold();
            c.Item().Text(titulo).FontSize(8).FontColor(Colors.Grey.Darken1);
        });

    private static void Seccion(ColumnDescriptor col, string titulo, IReadOnlyList<string> items)
    {
        col.Item().Text(titulo).SemiBold().FontColor(Colors.Blue.Darken3);
        foreach (var i in items) col.Item().Text("• " + i);
    }

    private static void Celda(IContainer c, string texto, bool encabezado = false)
    {
        var t = c.Background(encabezado ? Colors.Grey.Lighten3 : Colors.White)
                 .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4)
                 .Text(texto).FontSize(9);
        if (encabezado) t.Bold();
    }
}
