using EduMonitor.Common;
using EduMonitor.Identity.Api.Data;
using EduMonitor.Identity.Api.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContext<IdentityDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddEduMonitorAuth(builder.Configuration);
builder.Services.AddEduMonitorSwagger("MS-A · Identidad y Auditoría");
builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("sqlserver");

var app = builder.Build();

await DbSeeder.SeedAsync(app.Services, app.Configuration, app.Logger);

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { } // para pruebas de integración
