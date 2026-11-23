<#
 Respaldo de los datos de EduMonitor (SQL Server, MariaDB y Moodle) para compartirlos con el equipo.

 Uso (desde la raíz del repositorio, con Docker Desktop abierto):
   powershell -ExecutionPolicy Bypass -File .\scripts\respaldar-datos.ps1

 - Apaga el sistema para que las copias sean consistentes (después se enciende con: docker compose --profile ia --profile correo up -d).
 - Genera 4 archivos .tgz en la carpeta ..\respaldo-edumonitor, FUERA del repositorio, para no subirlos a Git por error.
 - No borra nada. El modelo de Ollama no se respalda: cada quien lo descarga con "ollama pull".
#>
$ErrorActionPreference = 'Stop'

if (-not (Test-Path docker-compose.yml)) { throw 'Ejecute el script desde la raíz del repositorio (donde está docker-compose.yml).' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop no está en ejecución. Ábralo y espere a que diga "Engine running".' }

$destino = Join-Path (Split-Path (Get-Location).Path -Parent) 'respaldo-edumonitor'
$volumenes = 'sqlserver_data', 'mariadb_data', 'moodle_data', 'moodledata_data'

Write-Host '1/3 Apagando el sistema para copiar datos consistentes...' -ForegroundColor Cyan
docker compose --profile ia --profile correo stop
if ($LASTEXITCODE -ne 0) { throw 'No se pudo apagar el sistema.' }

New-Item -ItemType Directory -Force -Path $destino | Out-Null
Write-Host "2/3 Copiando los volúmenes a: $destino" -ForegroundColor Cyan
foreach ($v in $volumenes) {
    $nombre = "edumonitor_$v"
    $existe = docker volume ls -q --filter ('name=^' + $nombre + '$')
    if (-not $existe) { throw "No existe el volumen $nombre. Revise los nombres con: docker volume ls" }
    docker run --rm -v "${nombre}:/data:ro" -v "${destino}:/backup" alpine sh -c "cd /data && tar czf /backup/$v.tgz ."
    if ($LASTEXITCODE -ne 0) { throw "Falló la copia de $nombre" }
    Write-Host "    OK  $v.tgz"
}

Write-Host '3/3 Listo. Archivos generados:' -ForegroundColor Green
Get-ChildItem $destino -Filter *.tgz | ForEach-Object { '    {0}  ({1:N0} MB)' -f $_.Name, ($_.Length / 1MB) }
Write-Host ''
Write-Host 'Comparta la carpeta completa (por ejemplo en un .zip por Drive) y el archivo .env por un canal privado (NO por GitHub).'
Write-Host 'Para volver a encender su sistema:  docker compose --profile ia --profile correo up -d'
