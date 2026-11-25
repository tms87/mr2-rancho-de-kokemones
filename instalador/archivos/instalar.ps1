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
    [string]$LigaLocal,
    # Pruebas: no crea ni toca accesos directos.
    [switch]$SinAccesos,
    # Idioma de los mensajes (es/en); si falta, el de la liga instalada.
    [string]$Idioma
)

$ErrorActionPreference = 'Stop'
$origen = $PSScriptRoot

if (-not $Idioma) { try { $Idioma = ([IO.File]::ReadAllText((Join-Path $Destino 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { } }
# Los mensajes del instalador salen en los dos idiomas (no hace falta elegir). T1: solo el idioma de la liga (accesos directos).
function T([string]$es, [string]$en) { if ($es -eq $en) { return $es }; if (($es.Length + $en.Length) -le 90) { "$es / $en" } else { "$es`n       $en" } }
function T1([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }
function Titulo($t) { Write-Host ''; Write-Host "== $($t -replace '\r?\n\s+', ' / ') ==" -ForegroundColor Cyan }
function Ok($t) { Write-Host "   OK  $t" -ForegroundColor Green }
function Aviso($t) { Write-Host "   !   $t" -ForegroundColor Yellow }
function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; exit 1 }

Write-Host (T 'MR2 Rancho de Kokemones: instalador' 'MR2 Rancho de Kokemones: installer') -ForegroundColor White

# ------------------------------------------------------------------
Titulo (T '1. Buscando el juego en Steam' '1. Looking for the game in Steam')
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
if (-not $steam) { Fallar (T 'No se encontro Steam. Instala Steam y el juego Monster Rancher 1&2 DX primero.' 'Steam was not found. Install Steam and Monster Rancher 1&2 DX first.') }
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
    Aviso (T 'No se encontro el juego automaticamente. Elegi el archivo MF2.exe (carpeta del juego\MF2\MF2.exe).' 'The game was not found automatically. Pick the MF2.exe file (game folder\MF2\MF2.exe).')
    Add-Type -AssemblyName System.Windows.Forms
    $dialogo = New-Object System.Windows.Forms.OpenFileDialog
    $dialogo.Filter = 'MF2.exe|MF2.exe'
    $dialogo.Title = (T 'Elegi MF2.exe (Monster Rancher 2 DX)' 'Pick MF2.exe (Monster Rancher 2 DX)')
    if ($dialogo.ShowDialog() -ne 'OK') { Fallar (T 'Instalacion cancelada.' 'Installation cancelled.') }
    $juego = $dialogo.FileName
}
Ok (T "Juego: $juego" "Game: $juego")

# ------------------------------------------------------------------
Titulo (T '2. Comprobando .NET 9 (lo necesita Reloaded-II)' '2. Checking .NET 9 (needed by Reloaded-II)')
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
    Aviso (T "Falta $($r.Nombre). Se descarga de Microsoft e instala (Windows puede pedir permiso)." "$($r.Nombre) is missing. Downloading it from Microsoft and installing it (Windows may ask for permission).")
    $archivo = Join-Path $env:TEMP ([IO.Path]::GetFileName($r.Url))
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $r.Url -OutFile $archivo -UseBasicParsing
    $proc = Start-Process $archivo -ArgumentList '/install', '/passive', '/norestart' -Verb RunAs -Wait -PassThru
    if ($proc.ExitCode -notin 0, 3010, 1638) { Fallar (T "No se pudo instalar $($r.Nombre) (codigo $($proc.ExitCode))." "Could not install $($r.Nombre) (code $($proc.ExitCode)).") }
    Ok (T "$($r.Nombre) instalado." "$($r.Nombre) installed.")
}
if ($faltan.Count -eq 0) { Ok (T '.NET 9 ya esta instalado.' '.NET 9 is already installed.') }

# ------------------------------------------------------------------
Titulo (T '2b. Comprobando Visual C++ (lo necesita el juego)' '2b. Checking Visual C++ (needed by the game)')
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
    if (-not ($v.Dlls | Where-Object { -not (Test-Path (Join-Path $sys32 $_)) })) { Ok (T "$($v.Nombre) ya esta instalado." "$($v.Nombre) is already installed."); continue }

    Aviso (T "Falta $($v.Nombre). Se instala ahora (Windows puede pedir permiso)." "$($v.Nombre) is missing. Installing it now (Windows may ask for permission).")
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
        Ok (T "$($v.Nombre) instalado." "$($v.Nombre) installed.")
    }
    catch {
        Aviso (T "No se pudo instalar $($v.Nombre) ($($_.Exception.Message))." "Could not install $($v.Nombre) ($($_.Exception.Message)).")
        Aviso (T 'Solucion: abri el juego UNA vez desde Steam (Steam instala lo que falta) y despues usa el acceso directo.' 'Fix: open the game ONCE from Steam (Steam installs what is missing) and then use the shortcut.')
    }
}

# ------------------------------------------------------------------
Titulo (T '3. Copiando el Rancho de Kokemones' '3. Copying Rancho de Kokemones')
# ------------------------------------------------------------------
$reloaded = Join-Path $Destino 'Reloaded-II'
$estadoLiga = Join-Path $Destino 'liga-instalada.json'
$actualizacion = Test-Path $estadoLiga

# Liga de esta instalacion: la que se pasa, o la que ya estaba (actualizacion).
if ($LigaLocal) { $liga = [ordered]@{ local = (Resolve-Path $LigaLocal).Path } }
elseif ($LigaRepo) { $liga = [ordered]@{ repo = $LigaRepo; rama = $LigaRama } }
elseif ($actualizacion) { $liga = (Get-Content $estadoLiga -Raw | ConvertFrom-Json).liga }
else { Fallar (T 'Falta la liga: este instalador se usa desde el instalador de una liga.' 'Missing league: this installer is run from a league installer.') }

# Con el juego abierto los mods estan en uso y no se pueden reemplazar (la copia quedaria esperando).
while (Get-Process MF2 -ErrorAction SilentlyContinue) {
    Aviso (T 'El juego esta abierto: cerralo para poder actualizar los mods. Se sigue solo al cerrarlo.' 'The game is open: close it so the mods can be updated. It continues by itself once closed.')
    Start-Sleep -Seconds 5
}
New-Item -ItemType Directory -Force $Destino | Out-Null

# Respaldo de las grabaciones del juego (Documentos\KoeiTecmo\mfdx_en: ranuras e indice psdata001.bin) antes de que
# los mods las usen: una sola vez, la primera instalacion en esta carpeta (o la primera actualizacion que lo trae).
$respaldos = @('Respaldo de grabaciones', 'Save backup') | ForEach-Object { Join-Path $Destino $_ }
$grabaciones = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'KoeiTecmo\mfdx_en'
if (-not ($respaldos | Where-Object { Test-Path $_ }) -and (Test-Path (Join-Path $grabaciones 'bu00'))) {
    $respaldo = Join-Path $respaldos[$(if ($Idioma -eq 'en') { 1 } else { 0 })] (Get-Date -Format 'yyyy-MM-dd HH-mm')
    try {
        New-Item -ItemType Directory -Force $respaldo | Out-Null
        Copy-Item $grabaciones $respaldo -Recurse -Force
        Ok (T "Respaldo de tus grabaciones en $respaldo" "Backup of your saves in $respaldo")
    }
    catch { Aviso (T "No se pudo respaldar tus grabaciones ($($_.Exception.Message))." "Could not back up your saves ($($_.Exception.Message)).") }
}

function Copiar($desde, $hacia, [string[]]$extra) {
    robocopy $desde $hacia @extra /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fallar (T "Error copiando archivos (robocopy $LASTEXITCODE). Si el juego o Reloaded-II estan abiertos, cerralos y volve a intentar." "Error copying files (robocopy $LASTEXITCODE). If the game or Reloaded-II are open, close them and try again.") }
}

# Scripts, configuracion del Rancho y plantillas.
foreach ($f in 'jugar.ps1', 'sincronizar.ps1', 'dependencias.ps1', 'aplicar-config.ps1', 'subir.ps1', 'clave.ps1', 'clave-github.ps1',
               'icono.ico', 'kokedex.ico', 'rancho.json', 'desinstalar.ps1', 'Uninstaller.bat') {
    Copy-Item (Join-Path $origen $f) $Destino -Force
}
Copiar (Join-Path $origen 'configuracion') (Join-Path $Destino 'configuracion') @('/MIR')
Copiar (Join-Path $origen 'plantillas') (Join-Path $Destino 'plantillas') @('/MIR')
if (Test-Path (Join-Path $origen 'exportador')) { Copiar (Join-Path $origen 'exportador') (Join-Path $Destino 'exportador') @('/MIR') }
# La Kokedex de la instalacion la genera sincronizar.ps1 con los datos de la liga.
Copy-Item (Join-Path $origen 'Kokedex.html') (Join-Path $Destino 'plantillas\Kokedex.html') -Force

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
Ok ($(if ($actualizacion) { T "Actualizado en $Destino" "Updated in $Destino" } else { T "Instalado en $Destino" "Installed in $Destino" }))

# ------------------------------------------------------------------
Titulo (T '3b. Reloaded-II, mods de terceros y datos de la liga' '3b. Reloaded-II, third-party mods and league data')
# ------------------------------------------------------------------
Write-Host ('       ' + (T 'La primera vez se bajan unos 55 MB (Reloaded-II y More Monsters).' 'The first time about 55 MB are downloaded (Reloaded-II and More Monsters).'))
try { & (Join-Path $Destino 'sincronizar.ps1') -Instalacion $Destino -Estricto }
catch { Fallar (T "No se pudo preparar la instalacion: $($_.Exception.Message)" "Could not prepare the installation: $($_.Exception.Message)") }
Ok (T 'Reloaded-II, mods, datos y configuracion de la liga al dia.' 'Reloaded-II, mods, league data and settings up to date.')

# ------------------------------------------------------------------
Titulo (T '4. Configurando Reloaded-II' '4. Configuring Reloaded-II')
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
Ok (T 'Reloaded-II configurado (sin consola).' 'Reloaded-II configured (no console).')

# ------------------------------------------------------------------
# Accesos directos: siempre en la carpeta de instalacion (si el jugador los borra del escritorio, siguen ahi) y, al
# instalar, en el escritorio y en el menu Inicio. Las actualizaciones no tocan el escritorio ni el menu Inicio.
# ------------------------------------------------------------------
$shell = New-Object -ComObject WScript.Shell
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
# Carpeta donde "Rivales Kokemon" exporta tus monstruos al guardar (se crea ya, para que el acceso funcione).
$exportados = Join-Path $reloaded 'User\Mods\MRDX.Custom.Leagues\Exportados'
New-Item -ItemType Directory -Force $exportados | Out-Null
$accesos = @(
    @{ Nombre = 'MR2 Rancho de Kokemones'; Destino = $powershell; Icono = 'icono.ico'
       Argumentos = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'jugar.ps1')`""
       Descripcion = (T1 'MR2 Rancho de Kokemones (Monster Rancher 2 DX con mods)' 'MR2 Rancho de Kokemones (Monster Rancher 2 DX with mods)') },
    @{ Nombre = 'MR2 Subir exportados'; Destino = $powershell; Icono = 'icono.ico'
       Argumentos = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'subir.ps1')`""
       Descripcion = (T1 'Subir tus monstruos exportados para sumarlos como rivales o a las competencias' 'Upload your exported monsters so they can join as rivals or competitions') },
    @{ Nombre = 'MR2 Exportar grabacion'; Destino = (Join-Path $Destino 'exportador\ExportarGrabacion.exe'); Icono = 'icono.ico'; Argumentos = ''
       Descripcion = (T1 'Exportar los monstruos de una grabacion (cualquier ranura, tambien de antes del Rancho)' 'Export the monsters of a save (any slot, also from before the Rancho)') },
    @{ Nombre = 'Kokedex'; Destino = (Join-Path $Destino 'Kokedex.html'); Icono = 'kokedex.ico'; Argumentos = ''
       Descripcion = (T1 'Kokedex: guia del Rancho de Kokemones (como jugar, reglas y objetos)' 'Kokedex: Rancho de Kokemones guide (how to play, rules and items)') },
    @{ Nombre = 'Rivales Kokemon - Exportados'; Destino = $exportados; Icono = $null; Argumentos = ''
       Descripcion = (T1 'Monstruos exportados al guardar (para compartirlos)' 'Monsters exported when saving (to share them)') }
)
function Crear-Accesos([string]$carpeta) {
    New-Item -ItemType Directory -Force $carpeta | Out-Null
    foreach ($a in $accesos) {
        $lnk = $shell.CreateShortcut((Join-Path $carpeta "$($a.Nombre).lnk"))
        $lnk.TargetPath = $a.Destino
        $lnk.Arguments = $a.Argumentos
        $lnk.WorkingDirectory = $Destino
        if ($a.Icono) { $lnk.IconLocation = "$(Join-Path $Destino $a.Icono),0" }
        $lnk.Description = $a.Descripcion
        $lnk.Save()
    }
}
Crear-Accesos $Destino

# Pruebas: no se tocan los accesos directos del escritorio ni el registro de Windows.
if ($SinAccesos) {
    Write-Host ''
    Write-Host (T "Listo (sin accesos directos). Para jugar: $(Join-Path $Destino 'jugar.ps1')" "Done (no shortcuts). To play: $(Join-Path $Destino 'jugar.ps1')") -ForegroundColor Green
    exit 0
}

# ------------------------------------------------------------------
if (-not $SinAccesoDirecto) {
    Titulo (T '5. Creando accesos directos' '5. Creating shortcuts')
    Crear-Accesos ([Environment]::GetFolderPath('Desktop'))
    Crear-Accesos (Join-Path ([Environment]::GetFolderPath('Programs')) 'MR2 Rancho de Kokemones')
    Ok (T "Accesos directos en el escritorio, en el menu Inicio (MR2 Rancho de Kokemones) y en $Destino." "Shortcuts on the desktop, in the Start menu (MR2 Rancho de Kokemones) and in $Destino.")
}

# Desinstalador: Uninstaller.bat en la carpeta de instalacion, y en "Aplicaciones instaladas" de Windows.
$desinstalar = Join-Path $Destino 'Uninstaller.bat'
try {
    $clave = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MR2RanchoDeKokemones'
    New-Item -Path $clave -Force | Out-Null
    $version = (Get-Content (Join-Path $Destino 'rancho.json') -Raw | ConvertFrom-Json).version
    $valores = @{ DisplayName = 'MR2 Rancho de Kokemones'; DisplayVersion = $version; Publisher = 'Rancho de Kokemones'
                  DisplayIcon = (Join-Path $Destino 'icono.ico'); InstallLocation = $Destino; UninstallString = "`"$desinstalar`"" }
    foreach ($v in $valores.GetEnumerator()) { Set-ItemProperty -Path $clave -Name $v.Key -Value $v.Value }
    Set-ItemProperty -Path $clave -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $clave -Name NoRepair -Value 1 -Type DWord
}
catch { Aviso (T "No se pudo registrar el desinstalador en Windows ($($_.Exception.Message)); igual esta en $desinstalar." "Could not register the uninstaller in Windows ($($_.Exception.Message)); it is still in $desinstalar.") }

Write-Host ''
Write-Host (T 'Listo. Para jugar usa el acceso directo "MR2 Rancho de Kokemones".' 'Done. To play, use the "MR2 Rancho de Kokemones" shortcut.') -ForegroundColor Green
Write-Host (T 'La primera vez tarda un poco mas: se preparan los datos del juego.' 'The first time takes a bit longer: the game data is prepared.') -ForegroundColor Green
