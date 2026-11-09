<#
 Sprint 2 · Reinicio ÚNICO de las bases de datos de EduMonitor.
 Motivo: en el Sprint 1 las tablas se crearon con EnsureCreated(); a partir del Sprint 2 se usan migraciones de EF Core
 y una base creada con EnsureCreated no se puede migrar. Moodle NO se toca: los datos se vuelven a traer al sincronizar.

 Requisito: Docker Desktop abierto y el contenedor de SQL Server encendido.
 Uso (desde la raíz del repositorio):   .\scripts\reset-databases.ps1
#>
$ErrorActionPreference = 'Stop'

if (-not (Test-Path .env)) { throw 'No se encontró .env. Ejecute el script desde la raíz del repositorio.' }
$claveSa = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''
if (-not $claveSa) { throw 'MSSQL_SA_PASSWORD no está definido en .env' }

Write-Host '0/3 Asegurando que SQL Server esté encendido...' -ForegroundColor Cyan
docker compose up -d sqlserver
Start-Sleep -Seconds 8

Write-Host '1/3 Deteniendo los servicios que usan las bases...' -ForegroundColor Cyan
docker compose stop gateway identity-api academic-api analytics-api frontend

Write-Host '2/3 Eliminando DB_Identidad, DB_Academico y DB_Analitica...' -ForegroundColor Cyan
$consulta = "IF DB_ID('DB_Identidad') IS NOT NULL BEGIN ALTER DATABASE DB_Identidad SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE DB_Identidad; END; IF DB_ID('DB_Academico') IS NOT NULL BEGIN ALTER DATABASE DB_Academico SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE DB_Academico; END; IF DB_ID('DB_Analitica') IS NOT NULL BEGIN ALTER DATABASE DB_Analitica SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE DB_Analitica; END;"
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $claveSa -C -Q $consulta
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron eliminar las bases. Revise la clave de SA en .env' }

Write-Host '3/3 Listo. Las bases se recrean con migraciones al levantar los servicios:' -ForegroundColor Green
Write-Host '    docker compose up -d --build'
