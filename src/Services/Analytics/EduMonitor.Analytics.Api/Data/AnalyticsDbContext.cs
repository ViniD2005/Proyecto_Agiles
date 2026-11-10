using EduMonitor.Analytics.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Data;

/// <summary>DB_Analitica. Sprint 2: modelo de lectura, indicadores y alertas. Recomendaciones y reportes llegan en el Sprint 3.</summary>
public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<MateriaAnalitica> Materias => Set<MateriaAnalitica>();
    public DbSet<EstudianteAnalitico> Estudiantes => Set<EstudianteAnalitico>();
    public DbSet<ActividadAnalitica> Actividades => Set<ActividadAnalitica>();
    public DbSet<CalificacionAnalitica> Calificaciones => Set<CalificacionAnalitica>();
    public DbSet<UnidadAnalitica> Unidades => Set<UnidadAnalitica>();
    public DbSet<TemaAnalitico> Temas => Set<TemaAnalitico>();
    public DbSet<ContenidoAnalitico> Contenidos => Set<ContenidoAnalitico>();
    public DbSet<MapeoAnalitico> Mapeos => Set<MapeoAnalitico>();
    public DbSet<UmbralMateria> Umbrales => Set<UmbralMateria>();
    public DbSet<IndicadorEstudiante> Indicadores => Set<IndicadorEstudiante>();
    public DbSet<AlertaRiesgo> Alertas => Set<AlertaRiesgo>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MateriaAnalitica>(e =>
        {
            e.ToTable("Materias");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nombre).HasMaxLength(255).IsRequired();
            e.Property(x => x.DocenteEmail).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.DocenteEmail);
        });

        b.Entity<EstudianteAnalitico>(e =>
        {
            e.ToTable("Estudiantes");
            e.HasKey(x => new { x.MateriaId, x.EstudianteId });
            e.Property(x => x.Nombres).HasMaxLength(100);
            e.Property(x => x.Apellidos).HasMaxLength(100);
        });

        b.Entity<ActividadAnalitica>(e =>
        {
            e.ToTable("Actividades");
            e.HasKey(x => new { x.MateriaId, x.ActividadId });
            e.Property(x => x.Nombre).HasMaxLength(255).IsRequired();
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Peso).HasPrecision(5, 2);
        });

        b.Entity<CalificacionAnalitica>(e =>
        {
            e.ToTable("Calificaciones");
            e.HasKey(x => new { x.MateriaId, x.ActividadId, x.EstudianteId });
            e.Property(x => x.Nota).HasPrecision(5, 2);
            e.Property(x => x.Estado).HasMaxLength(25).IsRequired();
        });

        b.Entity<UnidadAnalitica>(e =>
        {
            e.ToTable("Unidades");
            e.HasKey(x => new { x.MateriaId, x.UnidadId });
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        });

        b.Entity<TemaAnalitico>(e =>
        {
            e.ToTable("Temas");
            e.HasKey(x => new { x.MateriaId, x.TemaId });
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        });

        b.Entity<ContenidoAnalitico>(e =>
        {
            e.ToTable("Contenidos");
            e.HasKey(x => new { x.MateriaId, x.ContenidoId });
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        });

        b.Entity<MapeoAnalitico>(e =>
        {
            e.ToTable("Mapeos");
            e.HasKey(x => new { x.MateriaId, x.ActividadId, x.ContenidoId });
        });

        b.Entity<UmbralMateria>(e =>
        {
            e.ToTable("Umbrales");
            e.HasKey(x => x.MateriaId);
            e.Property(x => x.MateriaId).ValueGeneratedNever();
            e.Property(x => x.Critico).HasPrecision(4, 2);
            e.Property(x => x.Aprobado).HasPrecision(4, 2);
        });

        b.Entity<IndicadorEstudiante>(e =>
        {
            e.ToTable("Indicadores");
            e.HasKey(x => new { x.MateriaId, x.EstudianteId });
            e.Property(x => x.NotaPonderada).HasPrecision(5, 2);
            e.Property(x => x.NotaAcumulada).HasPrecision(5, 2);
            e.Property(x => x.PesoEvaluado).HasPrecision(5, 2);
            e.Property(x => x.Nivel).HasMaxLength(15).IsRequired();
        });

        b.Entity<AlertaRiesgo>(e =>
        {
            e.ToTable("AlertasRiesgo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MateriaId, x.EstudianteId });
            e.Property(x => x.Tipo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Regla).HasMaxLength(500).IsRequired();
            e.Property(x => x.Evidencia).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Actividad).HasMaxLength(255);
            e.Property(x => x.Valor).HasPrecision(6, 2);
        });
    }
}
