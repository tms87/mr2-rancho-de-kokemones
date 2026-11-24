# Arma (y opcionalmente publica) una version del paquete "MR2 Rancho de Kokemones".
#
#   powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0              (solo arma el zip)
#   powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0 -Publicar    (y crea la version en GitHub)
#
# Toma como base el zip de la ultima version publicada (Reloaded-II, MRDX Base Mod, More Monsters, etc. no estan
# en el repositorio) y reemplaza lo propio: los mods compilados de mods\, el instalador y el lanzador.
# Necesita: .NET SDK (dotnet) y GitHub CLI (gh) con sesion iniciada.
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Publicar,
    [string]$Notas = ''
)

$ErrorActionPreference = 'Stop'
$Repo = 'tms87/mr2-rancho-de-kokemones'
$raiz = Split-Path $PSScriptRoot
$tmp = Join-Path $env:TEMP 'rk-publicar'
[version]$Version | Out-Null # valida el formato (1.0.0)

function Paso($t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }
function Robocopy-Ok($desde, $hacia, [string[]]$extra) {
    robocopy $desde $hacia @extra /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $LASTEXITCODE ($desde -> $hacia)" }
}

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force $tmp | Out-Null

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

# ------------------------------------------------------------------
Paso '2. Bajando la ultima version publicada (base)'
# ------------------------------------------------------------------
gh release download --repo $Repo --pattern '*.zip' --dir (Join-Path $tmp 'base')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo bajar la ultima version' }
Expand-Archive (Get-ChildItem (Join-Path $tmp 'base') -Filter '*.zip' | Select-Object -First 1).FullName (Join-Path $tmp 'base\x')
$paquete = (Get-ChildItem (Join-Path $tmp 'base\x') -Recurse -Filter 'Instalar.bat' | Select-Object -First 1).Directory.FullName
$archivos = Join-Path $paquete 'archivos'

# ------------------------------------------------------------------
Paso '3. Reemplazando mods, instalador y lanzador'
# ------------------------------------------------------------------
foreach ($c in $compilados) {
    Robocopy-Ok $c.FullName (Join-Path $archivos "Reloaded-II\Mods\$($c.Name)") @('/MIR')
    Write-Host "   OK  $($c.Name)" -ForegroundColor Green
}
# Mods propios que cambiaron de nombre: la base todavia los trae con el nombre viejo.
foreach ($viejo in @('MRDX.Custom.LigaKokemon')) {
    $dir = Join-Path $archivos "Reloaded-II\Mods\$viejo"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force; Write-Host "   --  $viejo (renombrado)" -ForegroundColor DarkGray }
}
Copy-Item (Join-Path $raiz 'instalador\Instalar.bat') $paquete -Force
Copy-Item (Join-Path $raiz 'instalador\LEEME.txt') $paquete -Force
Robocopy-Ok (Join-Path $raiz 'instalador\archivos') $archivos @('/E')
Set-Content -Path (Join-Path $archivos 'version.txt') -Value $Version -Encoding ASCII
Set-Content -Path (Join-Path $raiz 'instalador\archivos\version.txt') -Value $Version -Encoding ASCII

# ------------------------------------------------------------------
Paso '4. Armando el zip'
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
Write-Host "   OK  $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)" -ForegroundColor Green

if ($Publicar) {
    Paso "5. Publicando v$Version en GitHub"
    gh release create "v$Version" $zip --repo $Repo --title "Rancho de Kokemones $Version" --notes $Notas
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la version en GitHub' }
    Write-Host '   OK  Publicada. Los jugadores la reciben la proxima vez que abran el juego.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'No se publico (falta -Publicar). Para probarlo, descomprimi el zip y ejecuta Instalar.bat.' -ForegroundColor Yellow
}
