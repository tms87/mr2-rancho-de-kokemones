# Sube tus monstruos exportados al repositorio de tu liga (como un "issue" de GitHub), para que
# puedan sumarse como rivales o a las competencias. Cada exportado es una grabacion entera ("fecha Entrenador.json"):
# el juego lo crea al guardar. Elegis cuales subir; los ya subidos quedan anotados para no repetirlos.
# La clave es personal: la configura clave.ps1 (se abre solo la primera vez) con la que te pasaron por privado.
# Para pruebas: -Exportados (otra carpeta de exportados) y -Datos (carpeta con clave-issues.dat y subidos.txt).
param([string]$Exportados, [string]$Datos = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$Idioma = 'es'
try { $Idioma = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { }
function T([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }
# El repositorio de la liga de esta instalacion.
$repo = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-instalada.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).liga.repo
if (-not $repo) {
    Write-Host (T 'Esta instalacion usa una liga local: no hay repositorio donde subir exportados.' 'This installation uses a local league: there is no repository to upload exports to.') -ForegroundColor Yellow
    Read-Host (T 'Enter para cerrar' 'Press Enter to close') | Out-Null
    exit
}
$exportados = if ($Exportados) { $Exportados } else { Join-Path $PSScriptRoot 'Reloaded-II\User\Mods\MRDX.Custom.Leagues\Exportados' }
$subidosTxt = Join-Path $Datos 'subidos.txt'
$claveDat = Join-Path $Datos 'clave-issues.dat'

function Salir($mensaje, $color = 'Yellow') {
    Write-Host ''
    Write-Host $mensaje -ForegroundColor $color
    Read-Host (T 'Enter para cerrar' 'Press Enter to close') | Out-Null
    exit
}

Write-Host (T 'Subir exportados - MR2 Rancho de Kokemones' 'Upload exports - MR2 Rancho de Kokemones') -ForegroundColor Cyan
if (-not (Test-Path $claveDat)) {
    Write-Host (T 'Primero hay que configurar tu clave (una sola vez).' 'First you need to set up your key (only once).') -ForegroundColor Yellow
    Write-Host ''
    & (Join-Path $PSScriptRoot 'clave.ps1') -Destino $Datos
    Write-Host ''
}
if (-not (Test-Path $claveDat)) { Salir (T 'No hay clave configurada.' 'No key is set up.') 'Red' }
try {
    $seguro = ConvertTo-SecureString ((Get-Content $claveDat -Raw).Trim())
    $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))
}
catch { Salir (T 'No se pudo leer tu clave (puede que se haya configurado con otro usuario de Windows). Volve a configurarla con clave.ps1.' 'Could not read your key (it may have been set up with another Windows user). Set it up again with clave.ps1.') 'Red' }
if (-not (Test-Path $exportados)) { Salir (T "Todavia no hay exportados (se crean al guardar en el juego): $exportados" "No exports yet (they are created when saving in the game): $exportados") }

$subidos = @()
if (Test-Path $subidosTxt) { $subidos = @(Get-Content $subidosTxt | Where-Object { $_ }) }

# Exportados del formato nuevo (una grabacion entera por archivo), del mas nuevo al mas viejo.
$lista = @()
foreach ($f in Get-ChildItem $exportados -Filter '*.json' | Sort-Object Name -Descending) {
    try {
        $j = Get-Content $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $j.monstruos) { continue }
        $lista += [pscustomobject]@{
            Archivo    = $f.Name
            Entrenador = $j.entrenador
            Fecha      = $j.fecha
            Monstruos  = (@($j.monstruos) | ForEach-Object { $_.nombre }) -join ', '
            Subido     = $(if ($subidos -contains $f.Name) { 'si' } else { '' })
            Ruta       = $f.FullName
        }
    }
    catch { }
}
if ($lista.Count -eq 0) { Salir (T 'No hay exportados para subir. Se crean al guardar en el juego al empezar cada anio.' 'There are no exports to upload. They are created when saving in the game at the start of each year.') }

# Elegir: ventana con la lista (si no esta disponible, por numero en la consola).
$elegidos = @()
try {
    $elegidos = @($lista | Select-Object Archivo, Entrenador, Fecha, Subido, Monstruos |
        Out-GridView -Title (T 'Elegi los exportados a subir (Ctrl+clic para varios) y toca Aceptar' 'Pick the exports to upload (Ctrl+click for several) and press OK') -OutputMode Multiple)
    $elegidos = @($lista | Where-Object { $n = $_.Archivo; $elegidos | Where-Object { $_.Archivo -eq $n } })
}
catch {
    Write-Host ''
    for ($i = 0; $i -lt [Math]::Min($lista.Count, 30); $i++) {
        $e = $lista[$i]
        Write-Host ("{0,3}. {1}{2}" -f ($i + 1), $e.Archivo, $(if ($e.Subido) { T '  (ya subido)' '  (already uploaded)' } else { '' }))
    }
    $resp = Read-Host (T 'Numeros a subir, separados por coma (Enter = el mas nuevo)' 'Numbers to upload, comma separated (Enter = the newest)')
    if ($resp.Trim() -eq '') { $elegidos = @($lista[0]) }
    else { $elegidos = @($resp -split '[, ]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { $lista[[int]$_ - 1] } | Where-Object { $_ }) }
}
if ($elegidos.Count -eq 0) { Salir (T 'No elegiste ninguno.' 'You did not pick any.') }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$encabezados = @{
    Authorization          = "Bearer $clave"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'MR2-Rancho-de-Kokemones'
}
$ok = 0
foreach ($e in $elegidos) {
    $contenido = Get-Content $e.Ruta -Raw -Encoding UTF8
    if ($contenido.Length -gt 60000) { Write-Host (T "$($e.Archivo): es demasiado grande para subirlo, se saltea." "$($e.Archivo): too big to upload, skipped.") -ForegroundColor Yellow; continue }
    $cuerpo = "Exportado de **$($e.Entrenador)**, fecha del juego $($e.Fecha): $($e.Monstruos).`n`n" +
              "Lo lee el Editor Kokemon; no hace falta hacer nada aca.`n`n" + '```json' + "`n" + $contenido.Trim() + "`n" + '```'
    $pedido = @{ title = "Exportado: $([IO.Path]::GetFileNameWithoutExtension($e.Archivo))"; body = $cuerpo; labels = @('exportado') } |
        ConvertTo-Json -Depth 3
    try {
        $r = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/issues" -Headers $encabezados `
            -Body ([Text.Encoding]::UTF8.GetBytes($pedido)) -ContentType 'application/json; charset=utf-8'
        Add-Content -Path $subidosTxt -Value $e.Archivo -Encoding UTF8
        Write-Host (T "Subido: $($e.Archivo) (#$($r.number))" "Uploaded: $($e.Archivo) (#$($r.number))") -ForegroundColor Green
        $ok++
    }
    catch {
        Write-Host (T "$($e.Archivo): no se pudo subir ($($_.Exception.Message))." "$($e.Archivo): could not upload ($($_.Exception.Message)).") -ForegroundColor Red
    }
}
Salir (T "Listo: $ok de $($elegidos.Count) subidos." "Done: $ok of $($elegidos.Count) uploaded.") $(if ($ok -eq $elegidos.Count) { 'Green' } else { 'Yellow' })
