# Instalador del paquete "Rancho de Kokemones" (mods para Monster Rancher 1&2 DX de Steam) para una liga.
# Lo ejecuta el instalador de la liga (o el lanzador, al cambiar de version). Copia los scripts y los mods propios,
# baja Reloaded-II y los mods de terceros de su origen, trae los datos y la configuracion de la liga, configura
# Reloaded-II y crea los accesos directos.
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'MR2-Rancho-de-Kokemones'),
    [switch]$SinAccesoDirecto,
    # Lo usa el lanzador al actualizar: ya sabe donde esta el juego (no busca ni pregunta).
    [string]$RutaJuego,
    # Liga: repo de GitHub ("usuario/repo") y rama, o una carpeta local (pruebas). Al actualizar se omiten y se
    # usa la liga ya instalada.
    [string]$LigaRepo,
    [string]$LigaRama = 'main',
    [string]$LigaLocal
)

$ErrorActionPreference = 'Stop'
$origen = $PSScriptRoot

function Titulo($t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }
function Ok($t) { Write-Host "   OK  $t" -ForegroundColor Green }
function Aviso($t) { Write-Host "   !   $t" -ForegroundColor Yellow }
function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; exit 1 }

Write-Host 'MR2 Rancho de Kokemones: instalador' -ForegroundColor White

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
if ($RutaJuego -and (Test-Path $RutaJuego)) { $juego = (Resolve-Path $RutaJuego).Path }
foreach ($b in ($bibliotecas | Select-Object -Unique)) {
    if ($juego) { break }
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
Titulo '2b. Comprobando Visual C++ (lo necesita el juego)'
# ------------------------------------------------------------------
# Steam instala estas librerias la primera vez que se abre el juego DESDE Steam; si el juego nunca se abrio
# desde Steam faltan (p. ej. "MSVCR100.dll was not found", que pide el sqlite3.dll del juego).
# Se usa primero el instalador que Steam ya dejo en disco (Steamworks Shared\_CommonRedist) y si no, el de Microsoft.
$sys32 = if ([Environment]::Is64BitOperatingSystem) { Join-Path $env:WINDIR 'SysWOW64' } else { Join-Path $env:WINDIR 'System32' }
$vcpp = @(
    @{ Nombre = 'Visual C++ 2010 (32 bits)'; Dlls = @('msvcr100.dll'); Local = 'vcredist\2010\vcredist_x86.exe'
       Args = @('/q', '/norestart'); Url = 'https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe' },
    @{ Nombre = 'Visual C++ 2015-2022 (32 bits)'; Dlls = @('vcruntime140.dll', 'msvcp140.dll'); Local = 'vcredist\2022\VC_redist.x86.exe'
       Args = @('/install', '/quiet', '/norestart'); Url = 'https://aka.ms/vs/17/release/vc_redist.x86.exe' }
)
foreach ($v in $vcpp) {
    if (-not ($v.Dlls | Where-Object { -not (Test-Path (Join-Path $sys32 $_)) })) { Ok "$($v.Nombre) ya esta instalado."; continue }

    Aviso "Falta $($v.Nombre). Se instala ahora (Windows puede pedir permiso)."
    $instalador = $null
    foreach ($b in ($bibliotecas | Select-Object -Unique)) {
        $c = Join-Path $b "steamapps\common\Steamworks Shared\_CommonRedist\$($v.Local)"
        if (Test-Path $c) { $instalador = $c; break }
    }
    try {
        if (-not $instalador) {
            $instalador = Join-Path $env:TEMP ([IO.Path]::GetFileName($v.Url))
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $v.Url -OutFile $instalador -UseBasicParsing
        }
        $proc = Start-Process $instalador -ArgumentList $v.Args -Verb RunAs -Wait -PassThru
        if ($proc.ExitCode -notin 0, 3010, 1638, 5100) { throw "codigo $($proc.ExitCode)" }
        Ok "$($v.Nombre) instalado."
    }
    catch {
        Aviso "No se pudo instalar $($v.Nombre) ($($_.Exception.Message))."
        Aviso 'Solucion: abri el juego UNA vez desde Steam (Steam instala lo que falta) y despues usa el acceso directo.'
    }
}

# ------------------------------------------------------------------
Titulo '3. Copiando el Rancho de Kokemones'
# ------------------------------------------------------------------
$reloaded = Join-Path $Destino 'Reloaded-II'
$estadoLiga = Join-Path $Destino 'liga-instalada.json'
$actualizacion = Test-Path $estadoLiga

# Liga de esta instalacion: la que se pasa, o la que ya estaba (actualizacion).
if ($LigaLocal) { $liga = [ordered]@{ local = (Resolve-Path $LigaLocal).Path } }
elseif ($LigaRepo) { $liga = [ordered]@{ repo = $LigaRepo; rama = $LigaRama } }
elseif ($actualizacion) { $liga = (Get-Content $estadoLiga -Raw | ConvertFrom-Json).liga }
else { Fallar 'Falta la liga: este instalador se usa desde el instalador de una liga.' }

# Con el juego abierto los mods estan en uso y no se pueden reemplazar (la copia quedaria esperando).
while (Get-Process MF2 -ErrorAction SilentlyContinue) {
    Aviso 'El juego esta abierto: cerralo para poder actualizar los mods. Se sigue solo al cerrarlo.'
    Start-Sleep -Seconds 5
}
New-Item -ItemType Directory -Force $Destino | Out-Null

function Copiar($desde, $hacia, [string[]]$extra) {
    robocopy $desde $hacia @extra /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fallar "Error copiando archivos (robocopy $LASTEXITCODE). Si el juego o Reloaded-II estan abiertos, cerralos y volve a intentar." }
}

# Scripts, configuracion del Rancho y plantillas.
foreach ($f in 'jugar.ps1', 'sincronizar.ps1', 'dependencias.ps1', 'aplicar-config.ps1', 'subir.ps1', 'clave.ps1',
               'Kokedex.html', 'icono.ico', 'kokedex.ico', 'rancho.json') {
    Copy-Item (Join-Path $origen $f) $Destino -Force
}
Copiar (Join-Path $origen 'configuracion') (Join-Path $Destino 'configuracion') @('/MIR')
Copiar (Join-Path $origen 'plantillas') (Join-Path $Destino 'plantillas') @('/MIR')

# Mods propios: se reemplazan enteros, menos los datos de la liga que viven en sus carpetas.
$datos = (Get-Content (Join-Path $origen 'configuracion\datos-liga.json') -Raw | ConvertFrom-Json).datos
foreach ($mod in Get-ChildItem (Join-Path $origen 'mods') -Directory) {
    $hacia = Join-Path $reloaded "Mods\$($mod.Name)"
    $excluir = @()
    foreach ($d in $datos) {
        $ruta = Join-Path $Destino ($d.destino -replace '/', '\')
        if (-not $ruta.StartsWith("$hacia\", [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ([IO.Path]::GetExtension($ruta)) { $excluir += '/XF', $ruta } else { $excluir += '/XD', $ruta }
    }
    Copiar $mod.FullName $hacia (@('/MIR') + $excluir)
}

# A que liga pertenece esta instalacion (lo lee el lanzador).
[IO.File]::WriteAllText($estadoLiga, ([pscustomobject]@{ liga = $liga } | ConvertTo-Json -Depth 3), (New-Object Text.UTF8Encoding $false))

# Los archivos bajados de internet quedan "bloqueados" por Windows: se desbloquean.
Get-ChildItem $Destino -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notlike "$reloaded\User\*" } |
    Unblock-File -ErrorAction SilentlyContinue
Ok ($(if ($actualizacion) { "Actualizado en $Destino" } else { "Instalado en $Destino" }))

# ------------------------------------------------------------------
Titulo '3b. Reloaded-II, mods de terceros y datos de la liga'
# ------------------------------------------------------------------
Write-Host '   La primera vez se bajan unos 55 MB (Reloaded-II y More Monsters).'
try { & (Join-Path $Destino 'sincronizar.ps1') -Instalacion $Destino -Estricto }
catch { Fallar "No se pudo preparar la instalacion: $($_.Exception.Message)" }
Ok 'Reloaded-II, mods, datos y configuracion de la liga al dia.'

# ------------------------------------------------------------------
Titulo '4. Configurando Reloaded-II'
# ------------------------------------------------------------------
function Reemplazar($texto) {
    $texto.Replace('{{RELOADED}}', ($reloaded -replace '\\', '\\')).
        Replace('{{JUEGO}}', ($juego -replace '\\', '\\')).
        Replace('{{CARPETA_JUEGO}}', ((Split-Path $juego) -replace '\\', '\\'))
}

# Modo portable: esta copia de Reloaded-II usa sus propias carpetas (Apps, Mods, User) aunque haya otra
# instalacion de Reloaded-II en la PC. Al lanzar el juego, Reloaded apunta la configuracion global a esta copia.
Set-Content -Path (Join-Path $reloaded 'portable.txt') -Value 'Modo portable de Reloaded-II (MR2 Rancho de Kokemones).'

# La configuracion global solo se crea si no existe: si ya hay otra instalacion de Reloaded-II no se toca
# (el lanzador ajusta lo necesario cada vez que se juega).
$ajustes = Join-Path $env:APPDATA 'Reloaded-Mod-Loader-II\ReloadedII.json'
New-Item -ItemType Directory -Force (Split-Path $ajustes) | Out-Null
if (-not (Test-Path $ajustes)) {
    $plantilla = Get-Content (Join-Path $origen 'plantillas\ReloadedII.json') -Raw
    [IO.File]::WriteAllText($ajustes, (Reemplazar $plantilla), (New-Object Text.UTF8Encoding $false))
}

$app = Join-Path $reloaded 'Apps\mf2.exe\AppConfig.json'
New-Item -ItemType Directory -Force (Split-Path $app) | Out-Null
$plantilla = Get-Content (Join-Path $origen 'plantillas\AppConfig.json') -Raw
[IO.File]::WriteAllText($app, (Reemplazar $plantilla), (New-Object Text.UTF8Encoding $false))
Ok 'Reloaded-II configurado (sin consola).'

# Una liga local es solo para pruebas: no se tocan los accesos directos del escritorio.
if ($liga.local) {
    Write-Host ''
    Write-Host "Listo (liga local, sin accesos directos). Para jugar: $(Join-Path $Destino 'jugar.ps1')" -ForegroundColor Green
    exit 0
}

# ------------------------------------------------------------------
if (-not $SinAccesoDirecto) {
    Titulo '5. Creando accesos directos'
    $shell = New-Object -ComObject WScript.Shell
    # Carpeta donde "Rivales Kokemon" exporta tus monstruos al guardar (se crea ya, para que el acceso funcione).
    $exportados = Join-Path $reloaded 'User\Mods\MRDX.Custom.Leagues\Exportados'
    New-Item -ItemType Directory -Force $exportados | Out-Null
    foreach ($carpeta in [Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) '')) {
        $lnk = $shell.CreateShortcut((Join-Path $carpeta 'Rivales Kokemon - Exportados.lnk'))
        $lnk.TargetPath = $exportados
        $lnk.Description = 'Monstruos exportados al guardar (para compartirlos)'
        $lnk.Save()

        $lnk = $shell.CreateShortcut((Join-Path $carpeta 'MR2 Rancho de Kokemones.lnk'))
        $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $lnk.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'jugar.ps1')`""
        $lnk.WorkingDirectory = $Destino
        $lnk.IconLocation = "$(Join-Path $Destino 'icono.ico'),0"
        $lnk.Description = 'MR2 Rancho de Kokemones (Monster Rancher 2 DX con mods)'
        $lnk.Save()
    }
    Ok 'Accesos directos "MR2 Rancho de Kokemones" y "Rivales Kokemon - Exportados" en el escritorio y en el menu Inicio.'
}

# Accesos para subir exportados y para la Kokedex: tambien en las actualizaciones automaticas (que no tocan los
# demas accesos), solo si todavia no existen.
$shell = New-Object -ComObject WScript.Shell
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$accesos = @(
    @{ Nombre = 'MR2 Subir exportados'; Destino = $powershell; Icono = 'icono.ico'
       Argumentos = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'subir.ps1')`""
       Descripcion = 'Subir tus monstruos exportados para sumarlos como rivales o a las competencias' },
    @{ Nombre = 'Kokedex'; Destino = (Join-Path $Destino 'Kokedex.html'); Icono = 'kokedex.ico'; Argumentos = ''
       Descripcion = 'Kokedex: guia del Rancho de Kokemones (como jugar, reglas y objetos)' }
)
foreach ($carpeta in [Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) '')) {
    foreach ($a in $accesos) {
        $ruta = Join-Path $carpeta "$($a.Nombre).lnk"
        if ((Test-Path $ruta) -and $SinAccesoDirecto) { continue }
        $lnk = $shell.CreateShortcut($ruta)
        $lnk.TargetPath = $a.Destino
        $lnk.Arguments = $a.Argumentos
        $lnk.WorkingDirectory = $Destino
        $lnk.IconLocation = "$(Join-Path $Destino $a.Icono),0"
        $lnk.Description = $a.Descripcion
        $lnk.Save()
    }
}

Write-Host ''
Write-Host 'Listo. Para jugar usa el acceso directo "MR2 Rancho de Kokemones".' -ForegroundColor Green
Write-Host 'La primera vez tarda un poco mas: se preparan los datos del juego.' -ForegroundColor Green
