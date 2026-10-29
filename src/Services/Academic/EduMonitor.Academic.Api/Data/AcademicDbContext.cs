using EduMonitor.Academic.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Academic.Api.Data;

public class AcademicDbContext(DbContextOptions<AcademicDbContext> options) : DbContext(options)
{
    public DbSet<Materia> Materias => Set<Materia>();
    public DbSet<Estudiante> Estudiantes => Set<Estudiante>();
    public DbSet<Matricula> Matriculas => Set<Matricula>();
    public DbSet<Actividad> Actividades => Set<Actividad>();
    public DbSet<SincronizacionLog> Sincronizaciones => Set<SincronizacionLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Materia>(e =>
        {
            e.ToTable("Materias");
            e.HasIndex(x => x.MoodleCourseId).IsUnique();
            e.HasIndex(x => x.DocenteEmail);
            e.Property(x => x.Nombre).HasMaxLength(255).IsRequired();
            e.Property(x => x.NombreCorto).HasMaxLength(100);
            e.Property(x => x.DocenteEmail).HasMaxLength(150).IsRequired();
            e.Property(x => x.OrigenSincronizacion).HasMaxLength(255);
        });

        b.Entity<Estudiante>(e =>
        {
            e.ToTable("Estudiantes");
            e.HasIndex(x => x.MoodleUserId).IsUnique();
            e.Property(x => x.Nombres).HasMaxLength(100);
            e.Property(x => x.Apellidos).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(150);
        });

        b.Entity<Matricula>(e =>
        {
            e.ToTable("Matriculas");
            e.HasKey(x => new { x.MateriaId, x.EstudianteId });
            e.HasOne(x => x.Materia).WithMany(m => m.Matriculas).HasForeignKey(x => x.MateriaId);
            e.HasOne(x => x.Estudiante).WithMany(s => s.Matriculas).HasForeignKey(x => x.EstudianteId);
        });

        b.Entity<Actividad>(e =>
        {
            e.ToTable("Actividades");
            e.HasIndex(x => new { x.MateriaId, x.MoodleCmId }).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(255).IsRequired();
            e.Property(x => x.Modulo).HasMaxLength(30);
            e.Property(x => x.Seccion).HasMaxLength(255);
            e.Property(x => x.Peso).HasPrecision(5, 2);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Materia).WithMany(m => m.Actividades).HasForeignKey(x => x.MateriaId);
        });

        b.Entity<SincronizacionLog>(e =>
        {
            e.ToTable("SincronizacionLog");
            e.Property(x => x.Tipo).HasMaxLength(30);
            e.Property(x => x.Origen).HasMaxLength(255);
            e.Property(x => x.Mensaje).HasMaxLength(1000);
            e.Property(x => x.UsuarioEmail).HasMaxLength(150);
            e.HasIndex(x => x.Fecha);
        });
    }
}
