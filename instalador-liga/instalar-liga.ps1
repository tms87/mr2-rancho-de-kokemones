# Instalador de una liga del Rancho de Kokemones (lo ejecuta Instalar.bat).
#
# liga-origen.json (al lado de este archivo) dice cual es la liga: { "repo": "usuario/repo", "rama": "main" }.
# Lee liga.json de la liga para saber que version del Rancho usa (y de que repositorio, por si es un fork), baja
# ese paquete de GitHub y ejecuta su instalador, que se encarga del resto (juego, .NET, Reloaded-II, mods, datos
# y configuracion de la liga, accesos directos).
#
# El repositorio de la liga tiene que ser publico: los jugadores lo leen sin credenciales.
#
# Liga local (no recomendada: no se actualiza sola ni permite subir exportados): -LigaLocal con la carpeta de la liga,
# o liga-origen.json con { "local": "C:\\carpeta" }.
# Pruebas: -PaqueteLocal (zip del Rancho) evita GitHub y -SinAccesos no toca los accesos directos.
param(
    [string]$LigaLocal,
    [string]$PaqueteLocal,
    [string]$Destino,
    [switch]$SinAccesos
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$h = @{ 'User-Agent' = 'rancho-de-kokemones-instalador'; 'Accept' = 'application/vnd.github+json' }

# Idioma de los mensajes: el de liga-origen.json hasta leer la liga, despues el de la liga.
$Idioma = 'es'
try { $Idioma = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-origen.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { }
function T([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }
function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; exit 1 }

try {
    # ---- La liga ----
    if (-not $LigaLocal) {
        $origen = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-origen.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($origen.local) { $LigaLocal = $origen.local }
    }
    if ($LigaLocal) {
        $ligaTexto = [IO.File]::ReadAllText((Join-Path $LigaLocal 'liga.json'), [Text.Encoding]::UTF8)
        $ligaArgs = @("-LigaLocal", (Resolve-Path $LigaLocal).Path)
    }
    else {
        $rama =if ($origen.rama) { $origen.rama } else { 'main' }
        try {
            $commit = (Invoke-RestMethod "https://api.github.com/repos/$($origen.repo)/commits/$rama" -Headers $h -TimeoutSec 20).sha
            $ligaTexto = (Invoke-WebRequest "https://raw.githubusercontent.com/$($origen.repo)/$commit/liga.json" -UseBasicParsing `
                -Headers @{ 'User-Agent' = $h['User-Agent'] } -TimeoutSec 20).Content
        }
        catch { Fallar (T "No se pudo leer la liga $($origen.repo) ($($_.Exception.Message)). Revisa la conexion; si sigue, avisale al administrador de la liga (el repositorio tiene que ser publico)." "Could not read the league $($origen.repo) ($($_.Exception.Message)). Check your connection; if it persists, tell the league admin (the repository has to be public).") }
        $ligaArgs = @('-LigaRepo', $origen.repo, '-LigaRama', $rama)
    }
    $liga = $ligaTexto | ConvertFrom-Json
    if ($liga.idioma) { $Idioma = $liga.idioma }
    Write-Host (T "Liga: $($liga.nombre)" "League: $($liga.nombre)") -ForegroundColor White
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
        catch { Fallar (T "No se encontro la version $($liga.rancho.version) del Rancho en $($liga.rancho.repo). Avisale al administrador de la liga." "Rancho version $($liga.rancho.version) was not found in $($liga.rancho.repo). Tell the league admin.") }
        $zipInfo = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
        if (-not $zipInfo) { Fallar (T "La version $($liga.rancho.version) no tiene paquete para descargar." "Version $($liga.rancho.version) has no package to download.") }
        Write-Host (T "Bajando el paquete ($([math]::Round($zipInfo.size / 1MB, 1)) MB)..." "Downloading the package ($([math]::Round($zipInfo.size / 1MB, 1)) MB)...")
        Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip -UseBasicParsing -TimeoutSec 900
    }
    Expand-Archive $zip -DestinationPath (Join-Path $tmp 'x') -Force
    Get-ChildItem (Join-Path $tmp 'x') -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    $inst = Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'instalar.ps1' | Select-Object -First 1
    if (-not $inst) { Fallar (T 'El paquete del Rancho no tiene instalador.' 'The Rancho package has no installer.') }
}
catch { Fallar $_.Exception.Message }

# ---- Instalador del Rancho (en esta misma ventana) ----
$extra = @()
if ($Destino) { $extra += @("-Destino", $Destino) }
if ($SinAccesos) { $extra += '-SinAccesos' }
$extra += @('-Idioma', $Idioma)
& powershell -NoProfile -ExecutionPolicy Bypass -File $inst.FullName @ligaArgs @extra
$codigo = $LASTEXITCODE
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
exit $codigo
