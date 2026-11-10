using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduMonitor.Academic.Api.Data;

/// <summary>Solo para «dotnet ef migrations»: no abre conexión ni necesita variables de entorno.</summary>
public class AcademicDbContextFactory : IDesignTimeDbContextFactory<AcademicDbContext>
{
    public AcademicDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=DB_Academico;User Id=sa;Password=design-time;TrustServerCertificate=True;")
            .Options;
        return new AcademicDbContext(options);
    }
}
