using EduMonitor.Analytics.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Data;

/// <summary>Configuración de las tablas del Sprint 3 (agente, recomendaciones y reportes).</summary>
public static class Sprint3Model
{
    public static void Configurar(ModelBuilder b)
    {
        b.Entity<ConsultaAgente>(e =>
        {
            e.ToTable("ConsultasAgente");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MateriaId, x.FechaUtc });
            e.Property(x => x.DocenteEmail).HasMaxLength(150).IsRequired();
            e.Property(x => x.Pregunta).HasMaxLength(600).IsRequired();
            e.Property(x => x.Intencion).HasMaxLength(100).IsRequired();
            e.Property(x => x.Modelo).HasMaxLength(100);
            e.Property(x => x.Resultado).HasMaxLength(20).IsRequired();
        });

        b.Entity<InvocacionHerramienta>(e =>
        {
            e.ToTable("InvocacionesHerramienta");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ConsultaId);
            e.Property(x => x.Herramienta).HasMaxLength(60).IsRequired();
            e.Property(x => x.Parametros).HasMaxLength(500).IsRequired();
            e.Property(x => x.Resumen).HasMaxLength(500).IsRequired();
        });

        b.Entity<RecomendacionEstudiante>(e =>
        {
            e.ToTable("Recomendaciones");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MateriaId, x.EstudianteId }).IsUnique();
            e.Property(x => x.Nivel).HasMaxLength(15).IsRequired();
            e.Property(x => x.NotaPonderada).HasPrecision(5, 2);
            e.Property(x => x.Evidencia).IsRequired();
            e.Property(x => x.Contenido).IsRequired();
            e.Property(x => x.Fuente).HasMaxLength(15).IsRequired();
            e.Property(x => x.Estado).HasMaxLength(15).IsRequired();
            e.Property(x => x.GeneradaPor).HasMaxLength(150).IsRequired();
            e.Property(x => x.EditadaPor).HasMaxLength(150);
            e.Property(x => x.AprobadaPor).HasMaxLength(150);
            e.Property(x => x.EnviadaA).HasMaxLength(150);
        });

        b.Entity<ReporteCurso>(e =>
        {
            e.ToTable("ReportesCurso");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MateriaId, x.GeneradoUtc });
            e.Property(x => x.GeneradoPor).HasMaxLength(150).IsRequired();
            e.Property(x => x.Contenido).IsRequired();
        });
    }
}
