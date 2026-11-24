# Sube tus monstruos exportados al repositorio de tu liga (como un "issue" de GitHub), para que
# puedan sumarse como rivales o a las competencias. Cada exportado es una grabacion entera ("fecha Entrenador.json"):
# el juego lo crea al guardar. Elegis cuales subir; los ya subidos quedan anotados para no repetirlos.
# La clave es personal: la configura clave.ps1 (se abre solo la primera vez) con la que te pasaron por privado.
# Para pruebas: -Exportados (otra carpeta de exportados) y -Datos (carpeta con clave-issues.dat y subidos.txt).
param([string]$Exportados, [string]$Datos = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
# El repositorio de la liga de esta instalacion.
$repo = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-instalada.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).liga.repo
if (-not $repo) {
    Write-Host 'Esta instalacion usa una liga local: no hay repositorio donde subir exportados.' -ForegroundColor Yellow
    Read-Host 'Enter para cerrar' | Out-Null
    exit
}
$exportados = if ($Exportados) { $Exportados } else { Join-Path $PSScriptRoot 'Reloaded-II\User\Mods\MRDX.Custom.Leagues\Exportados' }
$subidosTxt = Join-Path $Datos 'subidos.txt'
$claveDat = Join-Path $Datos 'clave-issues.dat'

function Salir($mensaje, $color = 'Yellow') {
    Write-Host ''
    Write-Host $mensaje -ForegroundColor $color
    Read-Host 'Enter para cerrar' | Out-Null
    exit
}

Write-Host 'Subir exportados - MR2 Rancho de Kokemones' -ForegroundColor Cyan
if (-not (Test-Path $claveDat)) {
    Write-Host 'Primero hay que configurar tu clave (una sola vez).' -ForegroundColor Yellow
    Write-Host ''
    & (Join-Path $PSScriptRoot 'clave.ps1') -Destino $Datos
    Write-Host ''
}
if (-not (Test-Path $claveDat)) { Salir 'No hay clave configurada.' 'Red' }
try {
    $seguro = ConvertTo-SecureString ((Get-Content $claveDat -Raw).Trim())
    $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))
}
catch { Salir 'No se pudo leer tu clave (¿se configuro con otro usuario de Windows?). Volve a configurarla con clave.ps1.' 'Red' }
if (-not (Test-Path $exportados)) { Salir "Todavia no hay exportados (se crean al guardar en el juego): $exportados" }

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
if ($lista.Count -eq 0) { Salir 'No hay exportados para subir. Se crean al guardar en el juego (con la version 0.4.0 o mas nueva).' }

# Elegir: ventana con la lista (si no esta disponible, por numero en la consola).
$elegidos = @()
try {
    $elegidos = @($lista | Select-Object Archivo, Entrenador, Fecha, Subido, Monstruos |
        Out-GridView -Title 'Elegi los exportados a subir (Ctrl+clic para varios) y toca Aceptar' -OutputMode Multiple)
    $elegidos = @($lista | Where-Object { $n = $_.Archivo; $elegidos | Where-Object { $_.Archivo -eq $n } })
}
catch {
    Write-Host ''
    for ($i = 0; $i -lt [Math]::Min($lista.Count, 30); $i++) {
        $e = $lista[$i]
        Write-Host ("{0,3}. {1}{2}" -f ($i + 1), $e.Archivo, $(if ($e.Subido) { '  (ya subido)' } else { '' }))
    }
    $resp = Read-Host 'Numeros a subir, separados por coma (Enter = el mas nuevo)'
    if ($resp.Trim() -eq '') { $elegidos = @($lista[0]) }
    else { $elegidos = @($resp -split '[, ]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { $lista[[int]$_ - 1] } | Where-Object { $_ }) }
}
if ($elegidos.Count -eq 0) { Salir 'No elegiste ninguno.' }

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
    if ($contenido.Length -gt 60000) { Write-Host "$($e.Archivo): es demasiado grande para subirlo, se saltea." -ForegroundColor Yellow; continue }
    $cuerpo = "Exportado de **$($e.Entrenador)**, fecha del juego $($e.Fecha): $($e.Monstruos).`n`n" +
              "Lo lee el Editor Kokemon; no hace falta hacer nada aca.`n`n" + '```json' + "`n" + $contenido.Trim() + "`n" + '```'
    $pedido = @{ title = "Exportado: $([IO.Path]::GetFileNameWithoutExtension($e.Archivo))"; body = $cuerpo; labels = @('exportado') } |
        ConvertTo-Json -Depth 3
    try {
        $r = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/issues" -Headers $encabezados `
            -Body ([Text.Encoding]::UTF8.GetBytes($pedido)) -ContentType 'application/json; charset=utf-8'
        Add-Content -Path $subidosTxt -Value $e.Archivo -Encoding UTF8
        Write-Host "Subido: $($e.Archivo) (#$($r.number))" -ForegroundColor Green
        $ok++
    }
    catch {
        Write-Host "$($e.Archivo): no se pudo subir ($($_.Exception.Message))." -ForegroundColor Red
    }
}
Salir "Listo: $ok de $($elegidos.Count) subidos." $(if ($ok -eq $elegidos.Count) { 'Green' } else { 'Yellow' })
