# Pone la instalacion al dia con su liga. Lo corren el instalador (al terminar) y el lanzador (en cada arranque).
#
#   1. liga.json: se baja del repo de la liga (o de una carpeta local, para pruebas) y queda una copia en la
#      instalacion, para jugar sin internet.
#   2. Dependencias (Reloaded-II y mods de terceros): dependencias.ps1, con los cambios de link de la liga.
#   3. Datos de la liga (rivales, torneos, banners, competencias): configuracion\datos-liga.json dice donde va
#      cada uno. Solo se baja lo que cambio.
#   4. Configuracion de los mods: aplicar-config.ps1.
#
# La liga instalada esta en liga-instalada.json: { "liga": { "repo": "usuario/repo", "rama": "main" } } o
# { "liga": { "local": "C:\\carpeta\\de\\la\\liga" } }.
# -Estricto (instalador): si algo falla, corta con error. Sin -Estricto (lanzador): anota el error y sigue.
param(
    [Parameter(Mandatory)][string]$Instalacion,
    # liga.json ya bajado por el lanzador (para no bajarlo dos veces).
    [string]$LigaJson,
    [switch]$Estricto
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$registro = Join-Path $Instalacion 'actualizacion.log'
$ua = @{ 'User-Agent' = 'rancho-de-kokemones'; 'Accept' = 'application/vnd.github+json' }

function Anotar($texto) {
    try { Add-Content -Path $registro -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $texto" -Encoding UTF8 } catch { }
}
function Leer-Json($ruta) { [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) | ConvertFrom-Json }
function Paso($nombre, [scriptblock]$accion) {
    try { & $accion }
    catch {
        Anotar "$nombre - error: $($_.Exception.Message)"
        if ($Estricto) { throw "$nombre - $($_.Exception.Message)" }
    }
}

$origen = (Leer-Json (Join-Path $Instalacion 'liga-instalada.json')).liga
$ligaCopia = Join-Path $Instalacion 'liga.json'

# Ruta de un archivo de la liga en GitHub (sin bajar nada) o en la carpeta local.
function Url-Raw($ruta) {
    $partes = $ruta.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }
    return "https://raw.githubusercontent.com/$($origen.repo)/$($origen.rama)/$($partes -join '/')"
}

# ---- 1. liga.json ----
Paso 'liga.json' {
    if ($LigaJson) { if ($LigaJson -ne $ligaCopia) { Copy-Item $LigaJson $ligaCopia -Force } }
    elseif ($origen.local) { Copy-Item (Join-Path $origen.local 'liga.json') $ligaCopia -Force }
    else {
        $tmp = "$ligaCopia.nuevo"
        Invoke-WebRequest (Url-Raw 'liga.json') -OutFile $tmp -UseBasicParsing -Headers @{ 'User-Agent' = $ua['User-Agent'] } -TimeoutSec 15
        Leer-Json $tmp | Out-Null # que sea JSON valido antes de reemplazar la copia
        Move-Item $tmp $ligaCopia -Force
    }
}
if (-not (Test-Path $ligaCopia)) { throw 'No hay liga.json (sin internet y sin copia local).' }

# ---- 2. Dependencias ----
Paso 'Dependencias' {
    $r = & (Join-Path $PSScriptRoot 'dependencias.ps1') -Manifiesto (Join-Path $Instalacion 'configuracion\dependencias.json') `
        -Liga $ligaCopia -Instalacion $Instalacion
    foreach ($d in $r) { Anotar "Dependencia instalada: $d" }
}

# ---- 3. Datos de la liga ----
# SHA-1 de git de un archivo, para compararlo con el arbol del repositorio sin bajarlo. Los textos se comparan
# tambien con saltos de linea LF (asi se guardan en el repositorio).
function Sha-Git([byte[]]$bytes) {
    $cabecera = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $todo = New-Object byte[] ($cabecera.Length + $bytes.Length)
    [Array]::Copy($cabecera, $todo, $cabecera.Length)
    [Array]::Copy($bytes, 0, $todo, $cabecera.Length, $bytes.Length)
    return (([Security.Cryptography.SHA1]::Create().ComputeHash($todo) | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Igual-Que-Repo($archivo, $sha) {
    if (-not (Test-Path $archivo)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($archivo)
    if ((Sha-Git $bytes) -eq $sha) { return $true }
    if ($archivo -match '\.(json|txt|md)$') {
        $lf = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($bytes) -replace "`r`n", "`n"))
        if ((Sha-Git $lf) -eq $sha) { return $true }
    }
    return $false
}

Paso 'Datos' {
    $mapa = (Leer-Json (Join-Path $Instalacion 'configuracion\datos-liga.json')).datos
    $cambios = 0
    if ($origen.local) {
        foreach ($d in $mapa) {
            $desde = Join-Path $origen.local ($d.liga -replace '/', '\')
            $hacia = Join-Path $Instalacion ($d.destino -replace '/', '\')
            if (Test-Path $desde -PathType Leaf) { New-Item -ItemType Directory -Force (Split-Path $hacia) | Out-Null; Copy-Item $desde $hacia -Force; continue }
            if (-not (Test-Path $desde)) { continue }
            $modo = if ($d.borrarSobrantes) { '/MIR' } else { '/E' }
            robocopy $desde $hacia $modo /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy $LASTEXITCODE ($desde)" }
        }
        Anotar 'Datos de la liga copiados de la carpeta local.'
        return
    }
    $arbol = Invoke-RestMethod "https://api.github.com/repos/$($origen.repo)/git/trees/$($origen.rama)?recursive=1" -Headers $ua -TimeoutSec 15
    foreach ($d in $mapa) {
        $base = Join-Path $Instalacion ($d.destino -replace '/', '\')
        $blobs = @($arbol.tree | Where-Object { $_.type -eq 'blob' -and ($_.path -eq $d.liga -or $_.path.StartsWith("$($d.liga)/")) })
        if ($blobs.Count -eq 0) { continue }
        $esperados = @{}
        foreach ($b in $blobs) {
            $resto = $b.path.Substring($d.liga.Length).TrimStart('/')
            $destino = if ($resto) { Join-Path $base ($resto -replace '/', '\') } else { $base }
            $esperados[$destino.ToLowerInvariant()] = $true
            if (Igual-Que-Repo $destino $b.sha) { continue }
            New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null
            Invoke-WebRequest (Url-Raw $b.path) -OutFile $destino -UseBasicParsing -Headers @{ 'User-Agent' = $ua['User-Agent'] } -TimeoutSec 30
            $cambios++
            Anotar "Dato actualizado: $($b.path)"
        }
        # Lo que la liga quito (por ejemplo un rival).
        if ($d.borrarSobrantes -and (Test-Path $base -PathType Container)) {
            foreach ($f in Get-ChildItem $base -Recurse -File) {
                if (-not $esperados.ContainsKey($f.FullName.ToLowerInvariant())) {
                    Remove-Item $f.FullName -Force
                    $cambios++
                    Anotar "Dato quitado: $($f.FullName.Substring($Instalacion.Length + 1))"
                }
            }
        }
    }
    if ($cambios -gt 0) { Anotar "Datos de la liga al dia ($cambios cambios)." }
}

# ---- 4. Configuracion de los mods ----
Paso 'Configuracion' {
    $avisos = & (Join-Path $PSScriptRoot 'aplicar-config.ps1') -Opciones (Join-Path $Instalacion 'configuracion\opciones.json') `
        -Liga $ligaCopia -Reloaded (Join-Path $Instalacion 'Reloaded-II')
    foreach ($a in $avisos) { Anotar "Configuracion: $a" }
}
