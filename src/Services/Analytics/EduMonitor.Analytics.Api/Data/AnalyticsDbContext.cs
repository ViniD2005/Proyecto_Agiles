using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Analytics.Api.Data;

/// <summary>
/// DB_Analitica. Sprint 1: solo se crea la base. Entidades Indicador, AlertaRiesgo,
/// Recomendacion y Reporte se agregan en los Sprints 2 y 3.
/// </summary>
public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
}
