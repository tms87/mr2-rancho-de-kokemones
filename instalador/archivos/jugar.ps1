# Lanzador de "MR2 Rancho de Kokemones": se actualiza, abre Steam si hace falta y arranca el juego con los mods.
#
# Actualizacion (antes de arrancar; si no hay internet o algo falla, se juega igual con lo instalado):
#  1. Paquete: si en GitHub hay una version publicada mas nueva que version.txt, se baja el zip y se ejecuta su
#     instalador sobre esta carpeta (no toca partidas, configuracion de los mods ni exportados).
#  2. Datos: actualizacion.json (rama main) lista carpetas y archivos de datos (rivales, torneos, imagenes) que se
#     bajan si cambiaron, sin necesidad de publicar una version. Si esos datos piden un paquete mas nuevo
#     (paqueteMinimo), se espera a tener esa version.
param([switch]$SinActualizar)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$Repo = 'tms87/mr2-rancho-de-kokemones'
$Rama = 'main'
$Titulo = 'MR2 Rancho de Kokemones'
$Registro = Join-Path $PSScriptRoot 'actualizacion.log'

function Error-Ventana($texto) {
    [System.Windows.Forms.MessageBox]::Show($texto, $Titulo, 'OK', 'Error') | Out-Null
    exit 1
}

function Anotar($texto) {
    try { Add-Content -Path $Registro -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $texto" -Encoding UTF8 } catch { }
}

# Ventanita de estado (solo aparece si de verdad hay algo para bajar).
$script:ventana = $null
$script:etiqueta = $null
function Estado($texto) {
    if (-not $script:ventana) {
        $script:ventana = New-Object System.Windows.Forms.Form
        $script:ventana.Text = $Titulo
        $script:ventana.Width = 420; $script:ventana.Height = 110
        $script:ventana.StartPosition = 'CenterScreen'
        $script:ventana.FormBorderStyle = 'FixedDialog'
        $script:ventana.ControlBox = $false
        $script:ventana.TopMost = $true
        $script:etiqueta = New-Object System.Windows.Forms.Label
        $script:etiqueta.Dock = 'Fill'
        $script:etiqueta.TextAlign = 'MiddleCenter'
        $script:etiqueta.Font = New-Object System.Drawing.Font('Segoe UI', 10)
        $script:ventana.Controls.Add($script:etiqueta)
        $script:ventana.Show()
    }
    $script:etiqueta.Text = $texto
    [System.Windows.Forms.Application]::DoEvents()
}

function Cerrar-Estado { if ($script:ventana) { $script:ventana.Close(); $script:ventana = $null } }

function Version-Local {
    $f = Join-Path $PSScriptRoot 'version.txt'
    try { if (Test-Path $f) { return [version]((Get-Content $f -Raw).Trim()) } } catch { }
    return [version]'0.0.0'
}

# SHA-1 de git de un archivo ("blob <tamano>\0<contenido>"), para compararlo con el arbol del repositorio sin
# bajar nada. Los textos se comparan tambien con saltos de linea LF (asi se guardan en el repositorio).
function Sha-Git([byte[]]$bytes) {
    $cabecera = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $sha = [Security.Cryptography.SHA1]::Create()
    $todo = New-Object byte[] ($cabecera.Length + $bytes.Length)
    [Array]::Copy($cabecera, $todo, $cabecera.Length)
    [Array]::Copy($bytes, 0, $todo, $cabecera.Length, $bytes.Length)
    return (($sha.ComputeHash($todo) | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Igual-Que-Repo($archivo, $sha) {
    if (-not (Test-Path $archivo)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($archivo)
    if ((Sha-Git $bytes) -eq $sha) { return $true }
    if ($archivo -match '\.(json|txt|md)$') {
        $texto = [Text.Encoding]::UTF8.GetString($bytes) -replace "`r`n", "`n"
        $lf = [Text.Encoding]::UTF8.GetBytes($texto)
        if ((Sha-Git $lf) -eq $sha) { return $true }
    }
    return $false
}

function Url-Raw($ruta) {
    $partes = $ruta.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }
    return "https://raw.githubusercontent.com/$Repo/$Rama/$($partes -join '/')"
}

function Actualizar($juego) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    $h = @{ 'User-Agent' = 'mr2-rancho-de-kokemones-lanzador'; 'Accept' = 'application/vnd.github+json' }

    # ---- 1. Paquete ----
    $local = Version-Local
    try {
        $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers $h -TimeoutSec 8
        $remota = [version]($rel.tag_name.TrimStart('v'))
        # 0.3.0 es la primera version con este lanzador: nunca se 'actualiza' a una anterior.
        if ($remota -gt $local -and $remota -ge [version]'0.3.0') {
            $zipInfo = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
            if ($zipInfo) {
                Estado "Actualizando a la version $remota...`nBajando el paquete ($([math]::Round($zipInfo.size / 1MB)) MB)."
                Anotar "Paquete $local -> $remota"
                $tmp = Join-Path $env:TEMP "rancho-kokemones-$remota"
                if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
                New-Item -ItemType Directory -Force $tmp | Out-Null
                $zip = Join-Path $tmp 'paquete.zip'
                Invoke-WebRequest $zipInfo.browser_download_url -OutFile $zip -UseBasicParsing -TimeoutSec 900
                Estado "Actualizando a la version $remota...`nInstalando."
                Expand-Archive $zip -DestinationPath (Join-Path $tmp 'x') -Force
                $inst = Get-ChildItem (Join-Path $tmp 'x') -Recurse -Filter 'instalar.ps1' | Select-Object -First 1
                if (-not $inst) { throw 'el paquete no tiene instalar.ps1' }
                Get-ChildItem (Join-Path $tmp 'x') -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
                $p = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($inst.FullName)`"",
                    '-Destino', "`"$PSScriptRoot`"", '-SinAccesoDirecto', '-RutaJuego', "`"$juego`"") -WindowStyle Hidden -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw "el instalador termino con codigo $($p.ExitCode)" }
                Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
                $local = Version-Local
                Anotar "Paquete actualizado a $local"
            }
        }
    }
    catch { Anotar "No se pudo actualizar el paquete: $($_.Exception.Message)" }

    # ---- 2. Datos ----
    try {
        $cfg = Invoke-RestMethod (Url-Raw 'actualizacion.json') -Headers @{ 'User-Agent' = $h['User-Agent'] } -TimeoutSec 8
        if ($local -lt [version]$cfg.paqueteMinimo) {
            Anotar "Datos nuevos necesitan el paquete $($cfg.paqueteMinimo) (instalado: $local); se esperan a esa version."
            return
        }
        $arbol = Invoke-RestMethod "https://api.github.com/repos/$Repo/git/trees/$($Rama)?recursive=1" -Headers $h -TimeoutSec 10
        $cambios = 0
        foreach ($d in $cfg.datos) {
            $base = Join-Path $PSScriptRoot ($d.destino -replace '/', '\')
            $blobs = @($arbol.tree | Where-Object { $_.type -eq 'blob' -and ($_.path -eq $d.repo -or $_.path.StartsWith("$($d.repo)/")) })
            if ($blobs.Count -eq 0) { continue }
            $esperados = @{}
            foreach ($b in $blobs) {
                $resto = $b.path.Substring($d.repo.Length).TrimStart('/')
                $destino = if ($resto) { Join-Path $base ($resto -replace '/', '\') } else { $base }
                $esperados[$destino.ToLowerInvariant()] = $true
                if (Igual-Que-Repo $destino $b.sha) { continue }
                Estado "Actualizando datos...`n$($b.path.Split('/')[-1])"
                New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null
                Invoke-WebRequest (Url-Raw $b.path) -OutFile $destino -UseBasicParsing -TimeoutSec 30
                $cambios++
                Anotar "Dato actualizado: $($b.path)"
            }
            # Archivos que ya no estan en el repositorio (p. ej. un rival quitado).
            if ($d.borrarSobrantes -and (Test-Path $base -PathType Container)) {
                foreach ($f in Get-ChildItem $base -Recurse -File) {
                    if (-not $esperados.ContainsKey($f.FullName.ToLowerInvariant())) {
                        Remove-Item $f.FullName -Force
                        $cambios++
                        Anotar "Dato quitado: $($f.FullName.Substring($PSScriptRoot.Length + 1))"
                    }
                }
            }
        }
        if ($cambios -gt 0) { Anotar "Datos al dia ($cambios cambios)." }
    }
    catch { Anotar "No se pudieron actualizar los datos: $($_.Exception.Message)" }
}

try {
    $reloaded = Join-Path $PSScriptRoot 'Reloaded-II\Reloaded-II.exe'
    $app = Join-Path $PSScriptRoot 'Reloaded-II\Apps\mf2.exe\AppConfig.json'
    if (-not (Test-Path $reloaded) -or -not (Test-Path $app)) { Error-Ventana 'Falta la instalacion. Volve a ejecutar Instalar.bat.' }

    $juego = (Get-Content $app -Raw | ConvertFrom-Json).AppLocation
    if (-not (Test-Path $juego)) { Error-Ventana "No se encuentra el juego en:`n$juego`n`nSi lo moviste, volve a ejecutar Instalar.bat." }

    if (-not $SinActualizar) {
        try { Actualizar $juego } catch { Anotar "Error al actualizar: $($_.Exception.Message)" }
        Cerrar-Estado
    }

    # Sin Steam abierto el juego se cierra solo al arrancar.
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
        Start-Process 'steam://open/main'
        $limite = (Get-Date).AddSeconds(90)
        while (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
            if ((Get-Date) -gt $limite) { Error-Ventana 'Steam no se abrio. Abrilo a mano y volve a intentar.' }
            Start-Sleep -Seconds 1
        }
        Start-Sleep -Seconds 15 # que Steam termine de iniciar sesion
    }

    # Sin consola. Es un ajuste global de Reloaded-II: se fija en cada lanzamiento por si otra instalacion
    # de Reloaded-II lo cambio.
    $ajustes = Join-Path $env:APPDATA 'Reloaded-Mod-Loader-II\ReloadedII.json'
    if (Test-Path $ajustes) {
        $texto = Get-Content $ajustes -Raw
        if ($texto -match '"ShowConsole"\s*:\s*true') {
            $texto = $texto -replace '"ShowConsole"\s*:\s*true', '"ShowConsole": false'
            [IO.File]::WriteAllText($ajustes, $texto, (New-Object Text.UTF8Encoding $false))
        }
    }

    Start-Process $reloaded -ArgumentList '--launch', "`"$juego`"" -WorkingDirectory (Split-Path $reloaded)
}
catch {
    Cerrar-Estado
    Error-Ventana "No se pudo iniciar el juego:`n$($_.Exception.Message)"
}
