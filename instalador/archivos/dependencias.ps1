# Baja e instala los programas y mods de terceros (configuracion\dependencias.json del Rancho) desde su origen.
#
#   - Cada archivo se verifica con su huella SHA-256 antes de usarlo: si no coincide, no se instala.
#   - La liga puede cambiar el link (y la huella) de cualquiera en liga.json: "dependencias": { "<id>": { "url", "sha256" } }.
#   - Lo ya instalado con la misma huella no se vuelve a bajar (dependencias-instaladas.json en -Instalacion).
#   - Reloaded-II se copia sobre <Instalacion>\Reloaded-II sin borrar Apps, Mods ni User; cada mod reemplaza su
#     carpeta <Instalacion>\Reloaded-II\Mods\<id>.
#   - Los .7z se abren con 7zr.exe (7-Zip), que tambien es una dependencia verificada.
#
# Devuelve la lista de lo que instalo. Si algo falla, corta con un error (lo ya instalado queda).
param(
    [Parameter(Mandatory)][string]$Manifiesto,
    [string]$Liga,
    [Parameter(Mandatory)][string]$Instalacion,
    [string]$Cache = (Join-Path $env:TEMP 'rancho-kokemones-dependencias')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Leer-Json($ruta) { [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) | ConvertFrom-Json }
function Huella($ruta) { (Get-FileHash $ruta -Algorithm SHA256).Hash.ToLowerInvariant() }

$man = Leer-Json $Manifiesto
$cambios = $null
if ($Liga -and (Test-Path $Liga)) { $cambios = (Leer-Json $Liga).dependencias }

$estadoArchivo = Join-Path $Instalacion 'dependencias-instaladas.json'
$estado = @{}
if (Test-Path $estadoArchivo) {
    try { foreach ($p in (Leer-Json $estadoArchivo).PSObject.Properties) { $estado[$p.Name] = $p.Value } } catch { }
}
New-Item -ItemType Directory -Force $Cache | Out-Null
$reloaded = Join-Path $Instalacion 'Reloaded-II'
$instalado = New-Object System.Collections.Generic.List[string]

# Link y huella de una dependencia, con los cambios de la liga.
function Origen($d) {
    $url = $d.url; $sha = $d.sha256
    if ($cambios -and $cambios.($d.id)) {
        $c = $cambios.($d.id)
        if ($c.url) { $url = $c.url }
        if ($c.sha256) { $sha = $c.sha256 }
    }
    if (-not $sha) { throw "$($d.id): falta la huella SHA-256 (no se instala nada sin verificar)." }
    return @{ Url = $url; Sha = $sha.ToLowerInvariant() }
}

# Baja el archivo a la cache (si ya esta con la huella correcta, no lo baja) y lo verifica.
function Bajar($d, $o) {
    $nombre = [IO.Path]::GetFileName(([Uri]$o.Url).AbsolutePath)
    $archivo = Join-Path $Cache "$($d.id)-$nombre"
    if ((Test-Path $archivo) -and (Huella $archivo) -eq $o.Sha) { return $archivo }
    Invoke-WebRequest $o.Url -OutFile $archivo -UseBasicParsing -TimeoutSec 900
    $h = Huella $archivo
    if ($h -ne $o.Sha) {
        Remove-Item $archivo -Force
        throw "$($d.id): la huella del archivo bajado no coincide (esperada $($o.Sha), bajada $h). Link: $($o.Url)"
    }
    return $archivo
}

function Ya-Instalada($d, $o, $destino) { $estado[$d.id] -eq $o.Sha -and (Test-Path $destino) }

function Anotar($d, $o) {
    $estado[$d.id] = $o.Sha
    $orden = [ordered]@{}; foreach ($k in ($estado.Keys | Sort-Object)) { $orden[$k] = $estado[$k] }
    [IO.File]::WriteAllText($estadoArchivo, ([pscustomobject]$orden | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    $instalado.Add("$($d.id) $($d.version)")
}

# ---- Herramientas (7zr.exe) ----
$herramientas = Join-Path $Instalacion 'herramientas'
$7zr = Join-Path $herramientas '7zr.exe'
foreach ($h in $man.herramientas) {
    $o = Origen $h
    $destino = Join-Path $herramientas "$($h.id).exe"
    if (Ya-Instalada $h $o $destino) { continue }
    $archivo = Bajar $h $o
    New-Item -ItemType Directory -Force $herramientas | Out-Null
    Copy-Item $archivo $destino -Force
    Anotar $h $o
}

function Abrir($d, $archivo) {
    $tmp = Join-Path $Cache "x-$($d.id)"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force $tmp | Out-Null
    if ($d.formato -eq 'zip') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($archivo, $tmp)
    }
    elseif ($d.formato -eq '7z') {
        if (-not (Test-Path $7zr)) { throw '7zr.exe no esta instalado.' }
        & $7zr x $archivo "-o$tmp" -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$($d.id): 7-Zip no pudo abrir el archivo (codigo $LASTEXITCODE)." }
    }
    else { throw "$($d.id): formato desconocido '$($d.formato)'." }
    return $tmp
}

function Copiar($desde, $hacia, [string[]]$extra) {
    robocopy $desde $hacia /E @extra /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $LASTEXITCODE ($desde -> $hacia)" }
}

# ---- Reloaded-II y mods ----
foreach ($d in $man.dependencias) {
    $o = Origen $d
    $destino = if ($d.tipo -eq 'reloaded') { Join-Path $reloaded 'Reloaded-II.exe' } else { Join-Path $reloaded "Mods\$($d.id)" }
    if (Ya-Instalada $d $o $destino) { continue }
    $archivo = Bajar $d $o
    $tmp = Abrir $d $archivo
    if ($d.tipo -eq 'reloaded') {
        $exe = Get-ChildItem $tmp -Recurse -Filter 'Reloaded-II.exe' | Select-Object -First 1
        if (-not $exe) { throw "$($d.id): el archivo no trae Reloaded-II.exe." }
        Copiar $exe.Directory.FullName $reloaded @()
    }
    else {
        # La carpeta del mod es la que tiene su ModConfig.json (algunos archivos la traen adentro de otra).
        $cfg = Get-ChildItem $tmp -Recurse -Filter 'ModConfig.json' | Sort-Object { $_.FullName.Length } | Select-Object -First 1
        if (-not $cfg) { throw "$($d.id): el archivo no trae ModConfig.json." }
        $modId = (Leer-Json $cfg.FullName).ModId
        if ($modId -ne $d.id) { throw "$($d.id): el archivo trae otro mod ($modId)." }
        Copiar $cfg.Directory.FullName $destino @('/PURGE')
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Anotar $d $o
}

return $instalado
