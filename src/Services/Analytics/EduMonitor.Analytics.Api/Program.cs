using EduMonitor.Analytics.Api.Data;
using EduMonitor.Analytics.Api.Messaging;
using EduMonitor.Analytics.Api.Services;
using EduMonitor.Common;
using EduMonitor.Common.Messaging;
using EduMonitor.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContext<AnalyticsDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddEduMonitorAuth(builder.Configuration);
builder.Services.AddEduMonitorSwagger("MS-C · Analítica y Agente IA");
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AnalyticsDbContext>("sqlserver");

// Sprint 2: servicios de análisis y consumo de eventos de RabbitMQ
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddScoped<RecalculoService>();
builder.Services.AddScoped<SnapshotIngestor>();
builder.Services.AddScoped<AnaliticaService>();
builder.Services.AddHostedService<AnalyticsEventConsumer>();

var app = builder.Build();

// Migraciones de EF Core con reintentos (SQL Server tarda en iniciar). Reemplaza a EnsureCreated del Sprint 1.
await DatabaseMigrator.MigrateWithRetryAsync<AnalyticsDbContext>(app.Services, app.Logger);

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
