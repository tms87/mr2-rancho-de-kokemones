# Instalador de "Monster Rancher 2 - Rancho" (mods para Monster Rancher 1&2 DX de Steam).
# Lo ejecuta Instalar.bat. Copia Reloaded-II con los mods, lo configura y crea el acceso directo.
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'MR2-Rancho'),
    [switch]$SinAccesoDirecto
)

$ErrorActionPreference = 'Stop'
$origen = $PSScriptRoot

function Titulo($t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }
function Ok($t) { Write-Host "   OK  $t" -ForegroundColor Green }
function Aviso($t) { Write-Host "   !   $t" -ForegroundColor Yellow }
function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; exit 1 }

Write-Host 'Monster Rancher 2 - Rancho: instalador' -ForegroundColor White

# ------------------------------------------------------------------
Titulo '1. Buscando el juego en Steam'
# ------------------------------------------------------------------
$steam = $null
foreach ($clave in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
    try {
        $p = Get-ItemProperty $clave -ErrorAction Stop
        foreach ($nombre in 'SteamPath', 'InstallPath') {
            if ($p.$nombre -and (Test-Path $p.$nombre)) { $steam = $p.$nombre; break }
        }
    } catch { }
    if ($steam) { break }
}
if (-not $steam) { Fallar 'No se encontro Steam. Instala Steam y el juego Monster Rancher 1&2 DX primero.' }
$steam = $steam -replace '/', '\'

# Todas las bibliotecas de Steam (el juego puede estar en otro disco).
$bibliotecas = @($steam)
$vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
if (Test-Path $vdf) {
    foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
        $bibliotecas += ($m.Groups[1].Value -replace '\\\\', '\')
    }
}

$juego = $null
foreach ($b in ($bibliotecas | Select-Object -Unique)) {
    $candidato = Join-Path $b 'steamapps\common\mfdx\MF2\MF2.exe'
    if (Test-Path $candidato) { $juego = (Resolve-Path $candidato).Path; break }
}

if (-not $juego) {
    Aviso 'No se encontro el juego automaticamente. Elegi el archivo MF2.exe (carpeta del juego\MF2\MF2.exe).'
    Add-Type -AssemblyName System.Windows.Forms
    $dialogo = New-Object System.Windows.Forms.OpenFileDialog
    $dialogo.Filter = 'MF2.exe|MF2.exe'
    $dialogo.Title = 'Elegi MF2.exe (Monster Rancher 2 DX)'
    if ($dialogo.ShowDialog() -ne 'OK') { Fallar 'Instalacion cancelada.' }
    $juego = $dialogo.FileName
}
Ok "Juego: $juego"

# ------------------------------------------------------------------
Titulo '2. Comprobando .NET 9 (lo necesita Reloaded-II)'
# ------------------------------------------------------------------
function Tiene-Runtime($raiz) {
    $dir = Join-Path $raiz 'dotnet\shared\Microsoft.WindowsDesktop.App'
    if (-not (Test-Path $dir)) { return $false }
    foreach ($v in Get-ChildItem $dir -Directory) {
        $partes = $v.Name.Split('.')
        if ($partes[0] -eq '9' -and [int]$partes[2] -ge 8) { return $true }
    }
    return $false
}

$faltan = @()
if (-not (Tiene-Runtime $env:ProgramFiles)) { $faltan += @{ Nombre = '.NET 9 Desktop (64 bits)'; Url = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe' } }
if (-not (Tiene-Runtime ${env:ProgramFiles(x86)})) { $faltan += @{ Nombre = '.NET 9 Desktop (32 bits)'; Url = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x86.exe' } }

foreach ($r in $faltan) {
    Aviso "Falta $($r.Nombre). Se descarga de Microsoft e instala (Windows puede pedir permiso)."
    $archivo = Join-Path $env:TEMP ([IO.Path]::GetFileName($r.Url))
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $r.Url -OutFile $archivo -UseBasicParsing
    $proc = Start-Process $archivo -ArgumentList '/install', '/passive', '/norestart' -Verb RunAs -Wait -PassThru
    if ($proc.ExitCode -notin 0, 3010, 1638) { Fallar "No se pudo instalar $($r.Nombre) (codigo $($proc.ExitCode))." }
    Ok "$($r.Nombre) instalado."
}
if ($faltan.Count -eq 0) { Ok '.NET 9 ya esta instalado.' }

# ------------------------------------------------------------------
Titulo '3. Copiando Reloaded-II y los mods'
# ------------------------------------------------------------------
$reloaded = Join-Path $Destino 'Reloaded-II'
$actualizacion = Test-Path (Join-Path $reloaded 'Reloaded-II.exe')
New-Item -ItemType Directory -Force $Destino | Out-Null

# Programa y mods: se reemplazan siempre. Datos del jugador (User): solo se agregan los que no existen,
# asi una actualizacion no pisa la configuracion ni el estado guardado de cada uno.
robocopy (Join-Path $origen 'Reloaded-II') $reloaded /E /XD (Join-Path $origen 'Reloaded-II\User') /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "Error copiando archivos (robocopy $LASTEXITCODE)." }
robocopy (Join-Path $origen 'Reloaded-II\User') (Join-Path $reloaded 'User') /E /XC /XN /XO /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "Error copiando la configuracion (robocopy $LASTEXITCODE)." }
Copy-Item (Join-Path $origen 'jugar.ps1') $Destino -Force

# Los archivos bajados de internet quedan "bloqueados" por Windows: se desbloquean.
Get-ChildItem $Destino -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
Ok ($(if ($actualizacion) { "Actualizado en $Destino" } else { "Instalado en $Destino" }))

# ------------------------------------------------------------------
Titulo '4. Configurando Reloaded-II'
# ------------------------------------------------------------------
function Reemplazar($texto) {
    $texto.Replace('{{RELOADED}}', ($reloaded -replace '\\', '\\')).
        Replace('{{JUEGO}}', ($juego -replace '\\', '\\')).
        Replace('{{CARPETA_JUEGO}}', ((Split-Path $juego) -replace '\\', '\\'))
}

$ajustes = Join-Path $env:APPDATA 'Reloaded-Mod-Loader-II\ReloadedII.json'
New-Item -ItemType Directory -Force (Split-Path $ajustes) | Out-Null
if (Test-Path $ajustes) {
    $anterior = Get-Content $ajustes -Raw
    if ($anterior -notmatch [regex]::Escape(($reloaded -replace '\\', '\\'))) {
        $copia = "$ajustes.antes-de-mr2-rancho"
        Copy-Item $ajustes $copia -Force
        Aviso "Ya tenias Reloaded-II configurado en otro lugar; tu configuracion anterior quedo en: $copia"
    }
}
$plantilla = Get-Content (Join-Path $origen 'plantillas\ReloadedII.json') -Raw
[IO.File]::WriteAllText($ajustes, (Reemplazar $plantilla), (New-Object Text.UTF8Encoding $false))

$app = Join-Path $reloaded 'Apps\mf2.exe\AppConfig.json'
New-Item -ItemType Directory -Force (Split-Path $app) | Out-Null
$plantilla = Get-Content (Join-Path $origen 'plantillas\AppConfig.json') -Raw
[IO.File]::WriteAllText($app, (Reemplazar $plantilla), (New-Object Text.UTF8Encoding $false))
Ok 'Reloaded-II configurado (sin consola).'

# ------------------------------------------------------------------
if (-not $SinAccesoDirecto) {
    Titulo '5. Creando acceso directo'
    $shell = New-Object -ComObject WScript.Shell
    foreach ($carpeta in [Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) '')) {
        $lnk = $shell.CreateShortcut((Join-Path $carpeta 'Monster Rancher 2 (mods).lnk'))
        $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $lnk.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'jugar.ps1')`""
        $lnk.WorkingDirectory = $Destino
        $lnk.IconLocation = "$juego,0"
        $lnk.Description = 'Monster Rancher 2 DX con los mods del rancho'
        $lnk.Save()
    }
    Ok 'Acceso directo "Monster Rancher 2 (mods)" en el escritorio y en el menu Inicio.'
}

Write-Host ''
Write-Host 'Listo. Para jugar usa el acceso directo "Monster Rancher 2 (mods)".' -ForegroundColor Green
Write-Host 'La primera vez tarda un poco mas: se preparan los datos del juego.' -ForegroundColor Green
