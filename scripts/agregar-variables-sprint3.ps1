<#
 Agrega a tu .env las variables nuevas del Sprint 3 SIN tocar las que ya tienes.
 Uso (desde la raíz del repositorio):   .\scripts\agregar-variables-sprint3.ps1
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $raiz '.env'
if (-not (Test-Path $envFile)) { Write-Host "No existe .env. Copia primero .env.example a .env" -ForegroundColor Red; exit 1 }

$nuevas = [ordered]@{
  'LLM_PROVIDER'        = 'Ollama'
  'LLM_MODEL'           = 'qwen2.5:3b'
  'LLM_BASE_URL'        = 'http://host.docker.internal:11434'
  'LLM_API_KEY'         = ''
  'LLM_TIMEOUT_SECONDS' = '120'
  'SMTP_HOST'           = 'mailpit'
  'SMTP_PORT'           = '1025'
  'SMTP_USER'           = ''
  'SMTP_PASSWORD'       = ''
  'SMTP_FROM'           = 'no-reply@edumonitor.local'
  'SMTP_STARTTLS'       = 'false'
  'REPORTE_PERIODO'     = 'Primer parcial'
}

$contenido = Get-Content $envFile -Raw
$agregadas = @()
foreach ($k in $nuevas.Keys) {
  if ($contenido -notmatch "(?m)^\s*$k\s*=") {
    $agregadas += "$k=$($nuevas[$k])"
  }
}
if ($agregadas.Count -eq 0) { Write-Host "Tu .env ya tiene todas las variables del Sprint 3." -ForegroundColor Green; exit 0 }

$bloque = "`r`n# ===== Sprint 3 =====`r`n" + ($agregadas -join "`r`n") + "`r`n"
Add-Content -Path $envFile -Value $bloque -Encoding UTF8
Write-Host "Variables agregadas a .env:" -ForegroundColor Green
$agregadas | ForEach-Object { Write-Host "  $_" }
