# Arma (y opcionalmente publica) una version del paquete del Rancho de Kokemones.
#
#   powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0              (solo arma el zip)
#   powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0 -Publicar    (y crea la version en GitHub)
#
# El paquete trae solo lo propio: los mods compilados de mods\ (sin datos de liga), los scripts del instalador y del
# lanzador y la configuracion del Rancho (configuracion\). Reloaded-II y los mods de terceros los baja el instalador
# de su origen (configuracion\dependencias.json); los datos y la configuracion de cada liga vienen de su repositorio.
#
# Se publica en el repositorio del remoto "origin" (asi funciona igual en un fork), o en -Repo.
# Necesita: .NET SDK (dotnet) y GitHub CLI (gh) con sesion iniciada.
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Publicar,
    [string]$Notas = '',
    [string]$Repo
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$tmp = Join-Path $env:TEMP 'rk-publicar'
[version]$Version | Out-Null # valida el formato (1.0.0)

if (-not $Repo) {
    $url = (git -C $raiz remote get-url origin)
    if ($url -notmatch 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') { throw "No se reconoce el repositorio de GitHub del remoto origin ($url). Usa -Repo usuario/repo." }
    $Repo = $Matches[1]
}

function Paso($t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }
function Robocopy-Ok($desde, $hacia, [string[]]$extra) {
    robocopy $desde $hacia @extra /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $LASTEXITCODE ($desde -> $hacia)" }
}

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force $tmp | Out-Null
Write-Host "Rancho de Kokemones $Version ($Repo)" -ForegroundColor White

# ------------------------------------------------------------------
Paso '1. Compilando los mods'
# ------------------------------------------------------------------
# Se compila en una carpeta corta de TEMP (las rutas largas rompen la compilacion en Windows).
$compilados = @()
foreach ($mod in Get-ChildItem (Join-Path $raiz 'mods') -Directory) {
    $src = Join-Path $tmp "src\$($mod.Name)"
    Robocopy-Ok $mod.FullName $src @('/E', '/XD', 'bin', 'obj', 'build')
    $proyecto = Get-ChildItem $src -Recurse -Filter '*.csproj' | Select-Object -First 1
    dotnet build $proyecto.FullName -c Release --no-incremental -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "No compila $($mod.Name)" }
    $salida = Get-ChildItem (Join-Path $src 'build') -Directory | Select-Object -First 1
    $compilados += $salida
    Write-Host "   OK  $($salida.Name)" -ForegroundColor Green
}
# Exportador manual (comparte codigo con el mod Leagues: se compila desde una copia con la misma estructura).
$srcExp = Join-Path $tmp 'src\instalador\exportador'
Robocopy-Ok (Join-Path $raiz 'instalador\exportador') $srcExp @('/E', '/XD', 'bin', 'obj', 'build')
Robocopy-Ok (Join-Path $raiz 'instalador\archivos') (Join-Path $tmp 'src\instalador\archivos') @('icono.ico')
Robocopy-Ok (Join-Path $raiz 'mods\Leagues') (Join-Path $tmp 'src\mods\Leagues') @('/E', '/XD', 'bin', 'obj', 'build')
dotnet build (Join-Path $srcExp 'ExportarGrabacion.csproj') -c Release --no-incremental -v q -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'No compila el exportador' }
$exportador = Join-Path $srcExp 'build\exportador'
Get-ChildItem $exportador -Filter '*.pdb' | Remove-Item
Write-Host '   OK  exportador' -ForegroundColor Green

# ------------------------------------------------------------------
Paso '2. Armando el paquete'
# ------------------------------------------------------------------
$paquete = Join-Path $tmp 'paquete\MR2-Rancho-de-Kokemones'
$archivos = Join-Path $paquete 'archivos'
Robocopy-Ok (Join-Path $raiz 'instalador\archivos') $archivos @('/E')
Robocopy-Ok (Join-Path $raiz 'configuracion') (Join-Path $archivos 'configuracion') @('/E')
foreach ($c in $compilados) { Robocopy-Ok $c.FullName (Join-Path $archivos "mods\$($c.Name)") @('/E') }
Robocopy-Ok $exportador (Join-Path $archivos 'exportador') @('/E')
Copy-Item (Join-Path $raiz 'instalador\LEEME.txt') $paquete -Force
[IO.File]::WriteAllText((Join-Path $archivos 'rancho.json'),
    ([pscustomobject]@{ repo = $Repo; version = $Version } | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Write-Host "   OK  $((Get-ChildItem $paquete -Recurse -File).Count) archivos" -ForegroundColor Green

# ------------------------------------------------------------------
Paso '3. Armando el zip'
# ------------------------------------------------------------------
$zip = Join-Path $tmp 'MR2-Rancho-de-Kokemones.zip'
# Entrada por entrada con rutas "/" (el formato estandar): Compress-Archive y ZipFile de Windows PowerShell guardan
# las rutas con "\" y algunos programas no las abren bien.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$padre = Split-Path $paquete -Parent
$flujo = [IO.File]::Open($zip, 'Create')
$archivoZip = New-Object IO.Compression.ZipArchive($flujo, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $paquete -Recurse -File) {
    $nombre = $f.FullName.Substring($padre.Length + 1).Replace('\', '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archivoZip, $f.FullName, $nombre, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$archivoZip.Dispose()
$flujo.Dispose()
Write-Host "   OK  $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green

if ($Publicar) {
    Paso "4. Publicando v$Version en $Repo"
    gh release create "v$Version" $zip --repo $Repo --title "Rancho de Kokemones $Version" --notes $Notas
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la version en GitHub' }
    Write-Host '   OK  Publicada. Las ligas que fijen esta version la instalan en el proximo arranque de cada jugador.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'No se publico (falta -Publicar). Para probarlo: instalador-liga\instalar-liga.ps1 -LigaLocal <carpeta> -PaqueteLocal <zip>.' -ForegroundColor Yellow
}
