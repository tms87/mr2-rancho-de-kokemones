# Desinstala MR2 Rancho de Kokemones (lo ejecuta Uninstaller.bat desde una copia en TEMP, para poder borrar la carpeta).
#
# Borra la carpeta de instalacion, los accesos directos (escritorio, menu Inicio y carpeta) y el registro en
# "Aplicaciones instaladas" de Windows. No toca el juego ni sus partidas (estan en la carpeta del juego, en Steam).
# Los exportados viven en la instalacion: antes de borrar se ofrece guardar una copia en Documentos.
param([Parameter(Mandatory)][string]$Instalacion, [switch]$Si)
$ErrorActionPreference = 'Stop'
$Instalacion = [IO.Path]::GetFullPath($Instalacion.TrimEnd('\', '.') )
$Idioma = 'es'
try { $Idioma = ([IO.File]::ReadAllText((Join-Path $Instalacion 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { }
function T([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }
function Salir($texto, $color = 'Yellow') {
    Write-Host ''; Write-Host $texto -ForegroundColor $color
    if (-not $Si) { Read-Host (T 'Enter para cerrar' 'Press Enter to close') | Out-Null }
    exit
}

Write-Host (T 'MR2 Rancho de Kokemones: desinstalador' 'MR2 Rancho de Kokemones: uninstaller') -ForegroundColor White
Write-Host ''
# Solo una instalacion del Rancho (tiene su lanzador y su liga).
if (-not (Test-Path (Join-Path $Instalacion 'jugar.ps1')) -or -not (Test-Path (Join-Path $Instalacion 'liga-instalada.json'))) {
    Salir (T "$Instalacion no parece una instalacion del Rancho de Kokemones: no se borra nada." "$Instalacion doesn't look like a Rancho de Kokemones installation: nothing is deleted.") 'Red'
}

Write-Host (T "Se va a borrar:" "This will delete:")
Write-Host "  - $Instalacion"
Write-Host (T '  - Los accesos directos del escritorio, del menu Inicio y de esa carpeta.' '  - The shortcuts on the desktop, in the Start menu and in that folder.')
Write-Host (T '  El juego y tus partidas no se tocan (estan en la carpeta del juego, en Steam).' '  The game and your saves are not touched (they are in the game folder, in Steam).')
Write-Host ''
if (-not $Si -and (Read-Host (T 'Desinstalar? (s/n)' 'Uninstall? (y/n)')) -notmatch '^(s|si|y|yes)$') { Salir (T 'No se desinstalo nada.' 'Nothing was uninstalled.') }

while (Get-Process MF2 -ErrorAction SilentlyContinue) {
    Write-Host (T 'El juego esta abierto: cerralo para seguir. Se sigue solo al cerrarlo.' 'The game is open: close it to continue. It continues by itself once closed.') -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}

# Exportados: una copia en Documentos, si hay.
$exportados = Join-Path $Instalacion 'Reloaded-II\User\Mods\MRDX.Custom.Leagues\Exportados'
if ((Test-Path $exportados) -and (Get-ChildItem $exportados -File -ErrorAction SilentlyContinue)) {
    if ($Si -or (Read-Host (T 'Guardar una copia de tus exportados en Documentos? (s/n)' 'Keep a copy of your exports in Documents? (y/n)')) -match '^(s|si|y|yes)$') {
        $copia = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MR2 Rancho de Kokemones - Exportados'
        New-Item -ItemType Directory -Force $copia | Out-Null
        Copy-Item (Join-Path $exportados '*') $copia -Recurse -Force
        Write-Host (T "Exportados copiados a $copia" "Exports copied to $copia") -ForegroundColor Green
    }
}

# Accesos directos que apuntan a esta instalacion (escritorio y menu Inicio; tambien los de versiones anteriores).
$shell = New-Object -ComObject WScript.Shell
$programas = [Environment]::GetFolderPath('Programs')
$nombres = 'MR2 Rancho de Kokemones', 'MR2 Subir exportados', 'Kokedex', 'Rivales Kokemon - Exportados'
foreach ($carpeta in [Environment]::GetFolderPath('Desktop'), $programas, (Join-Path $programas 'MR2 Rancho de Kokemones')) {
    foreach ($n in $nombres) {
        $lnk = Join-Path $carpeta "$n.lnk"
        if (-not (Test-Path $lnk)) { continue }
        $s = $shell.CreateShortcut($lnk)
        if ("$($s.TargetPath) $($s.Arguments) $($s.WorkingDirectory)" -like "*$Instalacion*") { Remove-Item $lnk -Force }
    }
}
$menu = Join-Path $programas 'MR2 Rancho de Kokemones'
if ((Test-Path $menu) -and -not (Get-ChildItem $menu)) { Remove-Item $menu -Force }

# Registro en "Aplicaciones instaladas".
$clave = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MR2RanchoDeKokemones'
if ((Test-Path $clave) -and ((Get-ItemProperty $clave).InstallLocation -eq $Instalacion)) { Remove-Item $clave -Recurse -Force }

# La carpeta de instalacion y la cache de descargas.
try { Remove-Item $Instalacion -Recurse -Force }
catch { Salir (T "No se pudo borrar todo $Instalacion ($($_.Exception.Message)). Cerra lo que la este usando y borrala a mano." "Could not delete all of $Instalacion ($($_.Exception.Message)). Close whatever is using it and delete it manually.") 'Red' }
Remove-Item (Join-Path $env:TEMP 'rancho-kokemones-dependencias') -Recurse -Force -ErrorAction SilentlyContinue

Salir (T 'Listo: MR2 Rancho de Kokemones se desinstalo.' 'Done: MR2 Rancho de Kokemones was uninstalled.') 'Green'
