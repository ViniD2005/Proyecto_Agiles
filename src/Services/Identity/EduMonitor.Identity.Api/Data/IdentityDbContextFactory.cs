using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduMonitor.Identity.Api.Data;

/// <summary>Solo para «dotnet ef migrations»: no abre conexión ni necesita variables de entorno.</summary>
public class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=DB_Identidad;User Id=sa;Password=design-time;TrustServerCertificate=True;")
            .Options;
        return new IdentityDbContext(options);
    }
}
