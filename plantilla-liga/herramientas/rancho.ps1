# Devuelve la carpeta con el codigo del Rancho de Kokemones que usa esta liga (liga.json, "rancho": repo y version).
# La primera vez lo baja de GitHub (el codigo de la version publicada) a TEMP; con -RanchoLocal usa esa carpeta.
# Lo usan editor.ps1, guia.ps1 e instalador.ps1.
param([string]$RanchoLocal)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if ($RanchoLocal) { return (Resolve-Path $RanchoLocal).Path }

$raiz = Split-Path $PSScriptRoot
$liga = [IO.File]::ReadAllText((Join-Path $raiz 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
$version = $liga.rancho.version
$trabajo = Join-Path $env:TEMP "EditorKokemon-$version"
$fuente = Join-Path $trabajo 'fuente'
if (-not (Test-Path (Join-Path $fuente 'configuracion'))) {
    Write-Host $(if ($liga.idioma -eq 'en') { "Downloading the Rancho $version source ($($liga.rancho.repo))..." } else { "Bajando el codigo del Rancho $version ($($liga.rancho.repo))..." })
    New-Item -ItemType Directory -Force $trabajo | Out-Null
    $zip = Join-Path $trabajo 'fuente.zip'
    Invoke-WebRequest "https://github.com/$($liga.rancho.repo)/archive/refs/tags/v$version.zip" -OutFile $zip -UseBasicParsing -TimeoutSec 300
    $x = Join-Path $trabajo 'x'
    if (Test-Path $x) { Remove-Item $x -Recurse -Force }
    Expand-Archive $zip -DestinationPath $x -Force
    if (Test-Path $fuente) { Remove-Item $fuente -Recurse -Force }
    Move-Item (Get-ChildItem $x -Directory | Select-Object -First 1).FullName $fuente
    Remove-Item $x, $zip -Recurse -Force -ErrorAction SilentlyContinue
}
return $fuente
