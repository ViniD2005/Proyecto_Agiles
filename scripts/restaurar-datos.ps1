<#
 Restaura en este equipo los datos que compartió otro integrante (SQL Server, MariaDB y Moodle).
 Deja el sistema igual al del compañero: usuarios, materias, estructura temática, notas, recomendaciones y Moodle ya configurado.

 Requisitos: Docker Desktop abierto, el archivo .env que le compartieron y la carpeta con los 4 archivos .tgz.
 Uso (desde la raíz del repositorio):
   powershell -ExecutionPolicy Bypass -File .\scripts\restaurar-datos.ps1 -Carpeta C:\ruta\respaldo-edumonitor
 Si la carpeta se llama respaldo-edumonitor y está junto a la del repositorio, no hace falta -Carpeta.

 ATENCIÓN: reemplaza los datos que EduMonitor tenga en este equipo. En una instalación nueva no hay nada que perder.
#>
param([string]$Carpeta = '')
$ErrorActionPreference = 'Stop'

if (-not (Test-Path docker-compose.yml)) { throw 'Ejecute el script desde la raíz del repositorio (donde está docker-compose.yml).' }
if (-not (Test-Path .env)) { throw 'Falta el archivo .env. Pídaselo a quien comparte los datos y cópielo en la raíz del repositorio.' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop no está en ejecución. Ábralo y espere a que diga "Engine running".' }

if (-not $Carpeta) { $Carpeta = Join-Path (Split-Path (Get-Location).Path -Parent) 'respaldo-edumonitor' }
if (-not (Test-Path $Carpeta)) { throw "No se encontró la carpeta: $Carpeta. Use -Carpeta con la ruta correcta." }
$Carpeta = (Resolve-Path $Carpeta).Path

$volumenes = 'sqlserver_data', 'mariadb_data', 'moodle_data', 'moodledata_data'
foreach ($v in $volumenes) {
    if (-not (Test-Path (Join-Path $Carpeta "$v.tgz"))) { throw "Falta $v.tgz en $Carpeta" }
}

Write-Host "Se restaurarán los datos desde: $Carpeta" -ForegroundColor Yellow
Write-Host 'Esto REEMPLAZA los datos actuales de EduMonitor, MariaDB y Moodle en este equipo.' -ForegroundColor Yellow
$resp = Read-Host 'Escriba SI para continuar'
if ($resp -ne 'SI') { throw 'Cancelado. No se cambió nada.' }

Write-Host '1/3 Apagando contenedores (los volúmenes no se borran)...' -ForegroundColor Cyan
docker compose --profile ia --profile correo down
if ($LASTEXITCODE -ne 0) { throw 'No se pudo apagar el sistema.' }

Write-Host '2/3 Creando los volúmenes (descarga las imágenes la primera vez; puede tardar varios minutos)...' -ForegroundColor Cyan
docker compose up --no-start sqlserver mariadb moodle
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron crear los contenedores. Revise su conexión a internet y el archivo .env.' }

Write-Host '3/3 Restaurando los datos...' -ForegroundColor Cyan
foreach ($v in $volumenes) {
    $nombre = "edumonitor_$v"
    docker run --rm -v "${nombre}:/data" -v "${Carpeta}:/backup:ro" alpine sh -c "find /data -mindepth 1 -delete; cd /data && tar xzf /backup/$v.tgz"
    if ($LASTEXITCODE -ne 0) { throw "Falló la restauración de $nombre" }
    Write-Host "    OK  $nombre"
}

Write-Host ''
Write-Host 'Datos restaurados. Siguiente paso, encender todo:' -ForegroundColor Green
Write-Host '    docker compose --profile ia --profile correo up -d --build'
