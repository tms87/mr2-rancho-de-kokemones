# Genera GUIA.md: la guia para jugadores (formato Discord) con la configuracion de esta liga, en su idioma.
#
# La plantilla es la del Rancho que usa la liga (configuracion\guia\guia.<idioma>.md de su codigo, que se baja de
# GitHub como para el editor) o la de -RanchoLocal. Valores: {{Mod.Opcion}} (configuracion de liga.json, o el valor
# por defecto del Rancho), {{liga.nombre}}, {{liga.repo}}, {{rancho.version}}. Una linea que empieza con
# {{?Mod.Opcion}} (o {{?!Mod.Opcion}}) solo queda si la opcion esta activada (o desactivada).
param([string]$RanchoLocal = $env:RANCHO_LOCAL)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$raiz = Split-Path $PSScriptRoot
$liga = [IO.File]::ReadAllText((Join-Path $raiz 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
$idioma = if ($liga.idioma -eq 'en') { 'en' } else { 'es' }

# ---- Codigo del Rancho (el mismo que usa el editor) ----
if ($RanchoLocal) { $fuente = (Resolve-Path $RanchoLocal).Path }
else {
    $version = $liga.rancho.version
    $trabajo = Join-Path $env:TEMP "EditorKokemon-$version"
    $fuente = Join-Path $trabajo 'fuente'
    if (-not (Test-Path (Join-Path $fuente 'configuracion\guia'))) {
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
}

# ---- Configuracion efectiva: la de la liga, o el valor por defecto (o fijo) del Rancho ----
$valores = @{
    'liga.nombre' = $liga.nombre; 'liga.repo' = ''; 'rancho.version' = $liga.rancho.version
}
$url = $null; try { $ErrorActionPreference = 'Continue'; $url = (git -C $raiz remote get-url origin 2>$null) } finally { $ErrorActionPreference = 'Stop' }
if ($url -match 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') { $valores['liga.repo'] = $Matches[1] }
$opciones = [IO.File]::ReadAllText((Join-Path $fuente 'configuracion\opciones.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
foreach ($m in $opciones.mods) {
    $corto = $m.id -replace '^MRDX\.(Custom|Game)\.', ''
    $elegidas = if ($liga.configuracion) { $liga.configuracion.($m.id) } else { $null }
    foreach ($o in $m.opciones) {
        $v = if ($o.clase -eq 'fija') { $o.valor } else { $o.defecto }
        if ($o.clase -eq 'liga' -and $elegidas -and ($elegidas.PSObject.Properties.Name -contains $o.clave)) { $v = $elegidas.($o.clave) }
        $valores["$corto.$($o.clave)"] = $v
    }
}
function Activa($clave) {
    $v = $valores[$clave]
    if ($v -is [bool]) { return $v }
    if ($v -is [string]) { return $v -ne '' }
    return $null -ne $v -and $v -ne 0
}

# ---- Plantilla ----
$salida = New-Object System.Collections.Generic.List[string]
foreach ($linea in [IO.File]::ReadAllLines((Join-Path $fuente "configuracion\guia\guia.$idioma.md"), [Text.Encoding]::UTF8)) {
    $l = $linea; $queda = $true
    while ($l -match '^\{\{\?(!?)([\w.]+)\}\}') {
        $activa = Activa $Matches[2]
        if (($Matches[1] -eq '!' -and $activa) -or ($Matches[1] -ne '!' -and -not $activa)) { $queda = $false }
        $l = $l.Substring($Matches[0].Length)
    }
    if (-not $queda) { continue }
    $l = [regex]::Replace($l, '\{\{([\w.]+)\}\}', { param($m) if ($valores.ContainsKey($m.Groups[1].Value)) { "$($valores[$m.Groups[1].Value])" } else { $m.Value } })
    $salida.Add($l)
}
$destino = Join-Path $raiz 'GUIA.md'
[IO.File]::WriteAllLines($destino, $salida, (New-Object Text.UTF8Encoding $false))
Write-Host $(if ($idioma -eq 'en') { "Guide generated: $destino" } else { "Guia generada: $destino" }) -ForegroundColor Green
