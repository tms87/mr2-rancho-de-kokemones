# Abre el Editor Kokemon (rivales, torneos, banners, competencias y subida a GitHub) sobre el repositorio de esta liga.
#
# El editor es parte del Rancho de Kokemones: se compila desde el codigo de la version que usa la liga (liga.json,
# "rancho": repo y version), que se baja de GitHub la primera vez. Con -RanchoLocal se usa una copia local del
# Rancho (para quien desarrolla un fork). Se compila en una carpeta corta de TEMP (las rutas largas rompen la
# compilacion en Windows). Necesita el .NET SDK 9 (dotnet) y git; para cerrar issues, GitHub CLI (gh).
param([string]$RanchoLocal = $env:RANCHO_LOCAL)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$raiz = Split-Path $PSScriptRoot
$liga = [IO.File]::ReadAllText((Join-Path $raiz 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
function T([string]$es, [string]$en) { if ($liga.idioma -eq 'en') { $en } else { $es } }
function Salir($t) { Write-Host $t -ForegroundColor Red; Read-Host (T 'Enter para cerrar' 'Press Enter to close') | Out-Null; exit 1 }

# ---- Codigo del editor ----
if ($RanchoLocal) {
    $fuente = (Resolve-Path $RanchoLocal).Path
    $trabajo = Join-Path $env:TEMP 'EditorKokemon-local'
}
else {
    $version = $liga.rancho.version
    $trabajo = Join-Path $env:TEMP "EditorKokemon-$version"
    $fuente = Join-Path $trabajo 'fuente'
    if (-not (Test-Path (Join-Path $fuente 'herramientas\EditorKokemon'))) {
        Write-Host (T "Bajando el editor del Rancho $version ($($liga.rancho.repo))..." "Downloading the Rancho $version editor ($($liga.rancho.repo))...")
        New-Item -ItemType Directory -Force $trabajo | Out-Null
        $zip = Join-Path $trabajo 'fuente.zip'
        try { Invoke-WebRequest "https://github.com/$($liga.rancho.repo)/archive/refs/tags/v$version.zip" -OutFile $zip -UseBasicParsing -TimeoutSec 300 }
        catch { Salir (T "No se pudo bajar el codigo del Rancho $version de $($liga.rancho.repo): $($_.Exception.Message)" "Could not download the Rancho $version source from $($liga.rancho.repo): $($_.Exception.Message)") }
        $x = Join-Path $trabajo 'x'
        if (Test-Path $x) { Remove-Item $x -Recurse -Force }
        Expand-Archive $zip -DestinationPath $x -Force
        $dir = Get-ChildItem $x -Directory | Select-Object -First 1
        if (Test-Path $fuente) { Remove-Item $fuente -Recurse -Force }
        Move-Item $dir.FullName $fuente
        Remove-Item $x, $zip -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- Compilar (si falta o si el codigo es mas nuevo) ----
$codigo = Join-Path $fuente 'herramientas\EditorKokemon'
$exe = Join-Path $trabajo 'build\EditorKokemon.exe'
$ultimo = (Get-ChildItem $codigo -File | Measure-Object LastWriteTime -Maximum).Maximum
if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $ultimo) {
    Write-Host (T 'Compilando el editor...' 'Building the editor...')
    $src = Join-Path $trabajo 'src'
    if (Test-Path $src) { Remove-Item $src -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $src 'herramientas\EditorKokemon'), (Join-Path $src 'instalador\archivos') | Out-Null
    Copy-Item (Join-Path $codigo '*') (Join-Path $src 'herramientas\EditorKokemon') -Recurse -Force -Exclude 'bin', 'obj', 'build'
    Copy-Item (Join-Path $fuente 'instalador\archivos\icono.ico') (Join-Path $src 'instalador\archivos') -Force
    dotnet build (Join-Path $src 'herramientas\EditorKokemon\EditorKokemon.csproj') -c Release -v q -nologo -o (Join-Path $trabajo 'build')
    if ($LASTEXITCODE -ne 0) { Salir (T 'No se pudo compilar el editor (falta el .NET SDK 9?).' 'Could not build the editor (is the .NET SDK 9 missing?).') }
}

# ---- Exportados recibidos ----
# Los que suben los jugadores ("MR2 Subir exportados"): issues abiertos con la etiqueta "exportado" en el repositorio
# de la liga. Se bajan a recibidos\ ("#numero titulo.json"); los de issues ya cerrados se borran.
$recibidos = Join-Path $raiz 'recibidos'
if (-not (Test-Path (Join-Path $raiz '.git'))) {
    Write-Host (T 'Esta carpeta no es un repositorio de git: no se bajan exportados recibidos ni se puede subir.' 'This folder is not a git repository: received exports are not downloaded and uploading is not possible.') -ForegroundColor Yellow
}
else { try {
    $url = (git -C $raiz remote get-url origin) 2>$null
    if ($url -match 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') {
        $repo = $Matches[1]
        $issues = @(Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/issues?labels=exportado&state=open&per_page=100" `
                -Headers @{ 'User-Agent' = 'rancho-de-kokemones-editor'; Accept = 'application/vnd.github+json' } -TimeoutSec 20)
        New-Item -ItemType Directory -Force $recibidos | Out-Null
        $abiertos = @()
        foreach ($i in $issues) {
            $m = [regex]::Match([string]$i.body, '```json\s*(.*?)\s*```', 'Singleline')
            if (-not $m.Success) { continue }
            $titulo = ([string]$i.title -replace '^Exportado:\s*', '') -replace '[\\/:*?"<>|]', '_'
            $archivo = Join-Path $recibidos ("#{0} {1}.json" -f $i.number, $titulo)
            $abiertos += $archivo
            if (-not (Test-Path $archivo)) {
                [IO.File]::WriteAllText($archivo, $m.Groups[1].Value, (New-Object Text.UTF8Encoding $false))
                Write-Host (T "Recibido: $([IO.Path]::GetFileName($archivo))" "Received: $([IO.Path]::GetFileName($archivo))")
            }
        }
        Get-ChildItem $recibidos -Filter '#*.json' | Where-Object { $abiertos -notcontains $_.FullName } | Remove-Item -Force
    }
}
catch {
    Write-Host (T "No se pudieron bajar los exportados recibidos: $($_.Exception.Message)" "Could not download the received exports: $($_.Exception.Message)") -ForegroundColor Yellow
} }

Start-Process $exe -ArgumentList "`"$raiz`""
