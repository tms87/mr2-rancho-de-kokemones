# Arma el instalador para los jugadores (Setup-<liga>.zip) y lo publica en el repositorio de la liga.
#
# Usa el instalador de la version del Rancho que fija la liga (liga.json), o el de -RanchoLocal, con el
# liga-origen.json de esta liga. En una liga de GitHub lo sube a la version "instalador-1" del repositorio
# (reemplaza el zip anterior; si no existe, la crea). En una liga local (sin remoto de GitHub) deja el zip al lado de
# la carpeta de la liga. Correlo de nuevo cuando cambie el instalador del Rancho (lo dicen las notas de su version).
# Necesita GitHub CLI (gh) con sesion iniciada para publicar.
param(
    [string]$RanchoLocal = $env:RANCHO_LOCAL,
    [switch]$SinPublicar,
    # Repositorio de la liga ("usuario/repo") si todavia no esta en el remoto origin (crear-liga sin publicar).
    [string]$Repo
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$liga = [IO.File]::ReadAllText((Join-Path $raiz 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
function T([string]$es, [string]$en) { if ($liga.idioma -eq 'en') { $en } else { $es } }

$fuente = & (Join-Path $PSScriptRoot 'rancho.ps1') -RanchoLocal $RanchoLocal
$kitFuente = Join-Path $fuente 'instalador-liga'
if (-not (Test-Path $kitFuente)) { throw (T "El Rancho $($liga.rancho.version) no tiene instalador de liga." "Rancho $($liga.rancho.version) has no league installer.") }

# Repositorio de la liga (remoto origin).
$repo = $Repo
if (-not $repo) {
    $url = $null
    try { $ErrorActionPreference = 'Continue'; $url = (git -C $raiz remote get-url origin 2>$null) } finally { $ErrorActionPreference = 'Stop' }
    if ($url -match 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') { $repo = $Matches[1] }
}
$nombre = if ($repo) { ($repo -split '/')[1] } else { Split-Path $raiz -Leaf }

# ---- El zip: Setup-<liga>/ con el instalador y liga-origen.json ----
$tmp = Join-Path $env:TEMP "liga-setup-$nombre"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
$carpeta = Join-Path $tmp "Setup-$nombre"
New-Item -ItemType Directory -Force $carpeta | Out-Null
Copy-Item (Join-Path $kitFuente '*') $carpeta -Force
$origen = if ($repo) { [ordered]@{ repo = $repo; rama = 'main'; idioma = $liga.idioma } } else { [ordered]@{ local = $raiz; idioma = $liga.idioma } }
[IO.File]::WriteAllText((Join-Path $carpeta 'liga-origen.json'), (([pscustomobject]$origen | ConvertTo-Json) + "`n"), (New-Object Text.UTF8Encoding $false))
$zip = Join-Path (Split-Path $raiz) "Setup-$nombre.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$flujo = [IO.File]::Open($zip, 'Create')
$archivo = New-Object IO.Compression.ZipArchive($flujo, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $carpeta -File) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archivo, $f.FullName, "Setup-$nombre/$($f.Name)") | Out-Null
}
$archivo.Dispose(); $flujo.Dispose()
Remove-Item $tmp -Recurse -Force
Write-Host (T "Instalador armado: $zip" "Installer built: $zip") -ForegroundColor Green

# ---- Publicarlo en la liga ----
if (-not $repo -or $SinPublicar) { return $zip }
$etiqueta = 'instalador-1'
$ErrorActionPreference = 'Continue'
$existe = (gh release view $etiqueta --repo $repo --json assets 2>$null)
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -eq 0 -and $existe) {
    gh release upload $etiqueta $zip --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw (T 'No se pudo subir el instalador.' 'Could not upload the installer.') }
    # Los zips anteriores con otro nombre (por ejemplo Instalar-<liga>.zip) se borran: queda uno solo.
    foreach ($a in ($existe | ConvertFrom-Json).assets) {
        if ($a.name -like '*.zip' -and $a.name -ne "Setup-$nombre.zip") { gh release delete-asset $etiqueta $a.name --repo $repo --yes | Out-Null }
    }
}
else {
    gh release create $etiqueta $zip --repo $repo --title (T 'Instalador de la liga' 'League installer') `
        --notes (T 'Descargá el zip, descomprimilo y ejecutá Setup.bat.' 'Download the zip, unzip it and run Setup.bat.')
    if ($LASTEXITCODE -ne 0) { throw (T 'No se pudo publicar el instalador.' 'Could not publish the installer.') }
}
Write-Host "https://github.com/$repo/releases/latest" -ForegroundColor Green
return $zip
