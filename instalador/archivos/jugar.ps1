# Lanzador del Rancho de Kokemones: pone la instalacion al dia con su liga, abre Steam si hace falta y arranca el
# juego con los mods.
#
# Actualizacion (antes de arrancar; si no hay internet o algo falla, se juega igual con lo instalado):
#  1. Se lee liga.json de la liga (liga-instalada.json dice cual es). Si la liga usa otra version del Rancho, u otro
#     repositorio (un fork), se baja ese paquete y se ejecuta su instalador sobre esta carpeta (no toca partidas
#     ni exportados).
#  2. Si no, sincronizar.ps1: dependencias, datos de la liga (rivales, torneos, banners, competencias) y
#     configuracion de los mods.
param(
    [switch]$SinActualizar,
    # Pruebas: no abre el juego, y usa este paquete del Rancho en vez de bajarlo de GitHub.
    [switch]$SinJuego,
    [string]$PaqueteLocal
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$Titulo = 'MR2 Rancho de Kokemones'
$Registro = Join-Path $PSScriptRoot 'actualizacion.log'
$Idioma = 'es'
try { $Idioma = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { }
function T([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }

function Error-Ventana($texto) {
    if ($SinJuego) { throw $texto }
    [System.Windows.Forms.MessageBox]::Show($texto, $Titulo, 'OK', 'Error') | Out-Null
    exit 1
}

function Anotar($texto) {
    try { Add-Content -Path $Registro -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $texto" -Encoding UTF8 } catch { }
}

function Leer-Json($ruta) { [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) | ConvertFrom-Json }

# Ventanita de estado (solo aparece si de verdad hay algo para hacer que tarde).
$script:ventana = $null
$script:etiqueta = $null
function Estado($texto) {
    if ($SinJuego) { Write-Host $texto; return }
    if (-not $script:ventana) {
        $script:ventana = New-Object System.Windows.Forms.Form
        $script:ventana.Text = $Titulo
        $script:ventana.Width = 420; $script:ventana.Height = 110
        $script:ventana.StartPosition = 'CenterScreen'
        $script:ventana.FormBorderStyle = 'FixedDialog'
        $script:ventana.ControlBox = $false
        $script:ventana.TopMost = $true
        $script:etiqueta = New-Object System.Windows.Forms.Label
        $script:etiqueta.Dock = 'Fill'
        $script:etiqueta.TextAlign = 'MiddleCenter'
        $script:etiqueta.Font = New-Object System.Drawing.Font('Segoe UI', 10)
        $script:ventana.Controls.Add($script:etiqueta)
        $script:ventana.Show()
    }
    $script:etiqueta.Text = $texto
    [System.Windows.Forms.Application]::DoEvents()
}

function Cerrar-Estado { if ($script:ventana) { $script:ventana.Close(); $script:ventana = $null } }

function Actualizar($juego) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    $h = @{ 'User-Agent' = 'rancho-de-kokemones-lanzador'; 'Accept' = 'application/vnd.github+json' }
    $origen = (Leer-Json (Join-Path $PSScriptRoot 'liga-instalada.json')).liga

    # ---- liga.json ----
    $ligaNueva = Join-Path $env:TEMP 'rancho-kokemones-liga.json'
    $liga = $null
    try {
        if ($origen.local) { Copy-Item (Join-Path $origen.local 'liga.json') $ligaNueva -Force }
        else {
            # El commit actual de la liga (raw.githubusercontent.com cachea las ramas unos minutos).
            $commit = (Invoke-RestMethod "https://api.github.com/repos/$($origen.repo)/commits/$($origen.rama)" -Headers $h -TimeoutSec 10).sha
            Invoke-WebRequest "https://raw.githubusercontent.com/$($origen.repo)/$commit/liga.json" -OutFile $ligaNueva `
                -UseBasicParsing -Headers @{ 'User-Agent' = $h['User-Agent'] } -TimeoutSec 10
        }
        $liga = Leer-Json $ligaNueva
    }
    catch { Anotar "No se pudo leer liga.json de la liga: $($_.Exception.Message). Se juega con lo instalado." }

    # ---- Version del Rancho que usa la liga ----
    if ($liga -and $liga.rancho) {
        $instalado = Leer-Json (Join-Path $PSScriptRoot 'rancho.json')
        $repo = $liga.rancho.repo; $version = $liga.rancho.version
        if ($repo -ne $instalado.repo -or $version -ne $instalado.version) {
            try {
                Anotar "Rancho $($instalado.repo) $($instalado.version) -> $repo $version"
                $tmp = Join-Path $env:TEMP "rancho-kokemones-$version"
                if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
                New-Item -ItemType Directory -Force $tmp | Out-Null
                $zip = Join-Path $tmp 'paquete.zip'
                if ($PaqueteLocal) { Copy-Item $PaqueteLocal $zip }
                else {
                    $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/v$version" -Headers $h -TimeoutSec 10
                    $zipInfo = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
                    if (-not $zipInfo) { throw "la version v$version de $repo no tiene paquete" }
                    Estado (T "Actualizando el Rancho a la version $version...`nBajando el paquete ($([math]::Round($zipInfo.size / 1MB)) MB)." "Updating the Rancho to version $version...`nDownloading the package ($([math]::Round($zipInfo.size / 1MB)) MB).")
                    Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip -UseBasicParsing -TimeoutSec 900
                }
                Estado (T "Actualizando el Rancho a la version $version...`nInstalando." "Updating the Rancho to version $version...`nInstalling.")
                Expand-Archive $zip -DestinationPath (Join-Path $tmp 'x') -Force
                $inst = Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'instalar.ps1' | Select-Object -First 1
                if (-not $inst) { throw 'el paquete no tiene instalar.ps1' }
                Get-ChildItem (Join-Path $tmp 'x') -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
                $p = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($inst.FullName)`"",
                    '-Destino', "`"$PSScriptRoot`"", '-SinAccesoDirecto', '-RutaJuego', "`"$juego`"") -WindowStyle Hidden -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw "el instalador termino con codigo $($p.ExitCode)" }
                Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
                Anotar "Rancho actualizado a $repo $version"
                return # el instalador ya sincronizo todo
            }
            catch { Anotar "No se pudo cambiar la version del Rancho: $($_.Exception.Message). Se sigue con la instalada." }
        }
    }

    # ---- Dependencias, datos y configuracion ----
    Estado (T 'Revisando la liga...' 'Checking the league...')
    $parametros = @{ Instalacion = $PSScriptRoot }
    if ($liga) { $parametros.LigaJson = $ligaNueva }
    if ($commit) { $parametros.Commit = $commit }
    & (Join-Path $PSScriptRoot 'sincronizar.ps1') @parametros
}

try {
    $reloaded = Join-Path $PSScriptRoot 'Reloaded-II\Reloaded-II.exe'
    $app = Join-Path $PSScriptRoot 'Reloaded-II\Apps\mf2.exe\AppConfig.json'
    if (-not (Test-Path $reloaded) -or -not (Test-Path $app)) { Error-Ventana (T 'Falta la instalacion. Volve a ejecutar el instalador de tu liga.' 'The installation is missing. Run your league installer again.') }

    $juego = (Get-Content $app -Raw | ConvertFrom-Json).AppLocation
    if (-not (Test-Path $juego)) { Error-Ventana (T "No se encuentra el juego en:`n$juego`n`nSi lo moviste, volve a ejecutar el instalador de tu liga." "The game was not found at:`n$juego`n`nIf you moved it, run your league installer again.") }

    if (-not $SinActualizar) {
        try { Actualizar $juego } catch { Anotar "Error al actualizar: $($_.Exception.Message)" }
        Cerrar-Estado
    }
    if ($SinJuego) { return }

    # Sin Steam abierto el juego se cierra solo al arrancar.
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
        Start-Process 'steam://open/main'
        $limite = (Get-Date).AddSeconds(90)
        while (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
            if ((Get-Date) -gt $limite) { Error-Ventana (T 'Steam no se abrio. Abrilo a mano y volve a intentar.' 'Steam did not open. Open it manually and try again.') }
            Start-Sleep -Seconds 1
        }
        Start-Sleep -Seconds 15 # que Steam termine de iniciar sesion
    }

    # Sin consola. Es un ajuste global de Reloaded-II: se fija en cada lanzamiento por si otra instalacion
    # de Reloaded-II lo cambio.
    $ajustes = Join-Path $env:APPDATA 'Reloaded-Mod-Loader-II\ReloadedII.json'
    if (Test-Path $ajustes) {
        $texto = Get-Content $ajustes -Raw
        if ($texto -match '"ShowConsole"\s*:\s*true') {
            $texto = $texto -replace '"ShowConsole"\s*:\s*true', '"ShowConsole": false'
            [IO.File]::WriteAllText($ajustes, $texto, (New-Object Text.UTF8Encoding $false))
        }
    }

    Start-Process $reloaded -ArgumentList '--launch', "`"$juego`"" -WorkingDirectory (Split-Path $reloaded)
}
catch {
    Cerrar-Estado
    Error-Ventana (T "No se pudo iniciar el juego:`n$($_.Exception.Message)" "Could not start the game:`n$($_.Exception.Message)")
}
