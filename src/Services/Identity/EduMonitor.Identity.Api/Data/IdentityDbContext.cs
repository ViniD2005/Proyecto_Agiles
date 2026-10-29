using EduMonitor.Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Identity.Api.Data;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Rol>(e =>
        {
            e.ToTable("Roles");
            e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Nombre).IsUnique();
            e.HasData(
                new Rol { Id = 1, Nombre = EduMonitor.Common.Roles.Docente },
                new Rol { Id = 2, Nombre = EduMonitor.Common.Roles.AdministradorTecnico });
        });

        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios");
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Rol).WithMany(r => r.Usuarios).HasForeignKey(x => x.RolId);
        });

        b.Entity<RegistroAuditoria>(e =>
        {
            e.ToTable("RegistroAuditoria");
            e.Property(x => x.Accion).HasMaxLength(60).IsRequired();
            e.Property(x => x.Servicio).HasMaxLength(20);
            e.Property(x => x.UsuarioEmail).HasMaxLength(150);
            e.Property(x => x.Detalle).HasMaxLength(1000);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.Fecha);
        });
    }
}
