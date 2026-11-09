using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduMonitor.Analytics.Api.Data;

/// <summary>Solo para «dotnet ef migrations»: no abre conexión ni necesita variables de entorno.</summary>
public class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=DB_Analitica;User Id=sa;Password=design-time;TrustServerCertificate=True;")
            .Options;
        return new AnalyticsDbContext(options);
    }
}
