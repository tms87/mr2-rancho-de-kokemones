# Instalador de una liga del Rancho de Kokemones (lo ejecuta Instalar.bat).
#
# liga-origen.json (al lado de este archivo) dice cual es la liga: { "repo": "usuario/repo", "rama": "main" }.
# Lee liga.json de la liga para saber que version del Rancho usa (y de que repositorio, por si es un fork), baja
# ese paquete de GitHub y ejecuta su instalador, que se encarga del resto (juego, .NET, Reloaded-II, mods, datos
# y configuracion de la liga, accesos directos).
#
# El repositorio de la liga tiene que ser publico: los jugadores lo leen sin credenciales.
#
# Pruebas: -LigaLocal (carpeta con la liga) y -PaqueteLocal (zip del Rancho) evitan GitHub.
param(
    [string]$LigaLocal,
    [string]$PaqueteLocal,
    [string]$Destino
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$h = @{ 'User-Agent' = 'rancho-de-kokemones-instalador'; 'Accept' = 'application/vnd.github+json' }

function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; exit 1 }

try {
    # ---- La liga ----
    if ($LigaLocal) {
        $ligaTexto = [IO.File]::ReadAllText((Join-Path $LigaLocal 'liga.json'), [Text.Encoding]::UTF8)
        $ligaArgs = @("-LigaLocal", (Resolve-Path $LigaLocal).Path)
    }
    else {
        $origen = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-origen.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
        $rama = if ($origen.rama) { $origen.rama } else { 'main' }
        try {
            $ligaTexto = (Invoke-WebRequest "https://raw.githubusercontent.com/$($origen.repo)/$rama/liga.json" -UseBasicParsing `
                -Headers @{ 'User-Agent' = $h['User-Agent'] } -TimeoutSec 20).Content
        }
        catch { Fallar "No se pudo leer la liga $($origen.repo) ($($_.Exception.Message)). Revisa la conexion; si sigue, avisale al administrador de la liga (el repositorio tiene que ser publico)." }
        $ligaArgs = @('-LigaRepo', $origen.repo, '-LigaRama', $rama)
    }
    $liga = $ligaTexto | ConvertFrom-Json
    Write-Host "Liga: $($liga.nombre)" -ForegroundColor White
    Write-Host "Rancho de Kokemones $($liga.rancho.version) ($($liga.rancho.repo))"

    # ---- El paquete del Rancho que usa la liga ----
    $tmp = Join-Path $env:TEMP 'rancho-kokemones-instalador'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $zip = Join-Path $tmp 'paquete.zip'
    if ($PaqueteLocal) { Copy-Item $PaqueteLocal $zip }
    else {
        try {
            $rel = Invoke-RestMethod "https://api.github.com/repos/$($liga.rancho.repo)/releases/tags/v$($liga.rancho.version)" -Headers $h -TimeoutSec 20
        }
        catch { Fallar "No se encontro la version $($liga.rancho.version) del Rancho en $($liga.rancho.repo). Avisale al administrador de la liga." }
        $zipInfo = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
        if (-not $zipInfo) { Fallar "La version $($liga.rancho.version) no tiene paquete para descargar." }
        Write-Host "Bajando el paquete ($([math]::Round($zipInfo.size / 1MB, 1)) MB)..."
        Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip -UseBasicParsing -TimeoutSec 900
    }
    Expand-Archive $zip -DestinationPath (Join-Path $tmp 'x') -Force
    Get-ChildItem (Join-Path $tmp 'x') -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    $inst = Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'instalar.ps1' | Select-Object -First 1
    if (-not $inst) { Fallar 'El paquete del Rancho no tiene instalador.' }
}
catch { Fallar $_.Exception.Message }

# ---- Instalador del Rancho (en esta misma ventana) ----
$extra = @()
if ($Destino) { $extra = @("-Destino", $Destino) }
& powershell -NoProfile -ExecutionPolicy Bypass -File $inst.FullName @ligaArgs @extra
$codigo = $LASTEXITCODE
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
exit $codigo
