# Abre el Editor Kokemon (rivales, torneos, banners y subida a GitHub) sobre este repositorio.
# La primera vez (o si cambio el codigo del editor) lo compila en una carpeta corta de TEMP: las rutas largas
# rompen la compilacion en Windows. Necesita el .NET SDK 9 (dotnet).
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$fuente = Join-Path $PSScriptRoot 'EditorKokemon'
$destino = Join-Path $env:TEMP 'EditorKokemon'
$exe = Join-Path $destino 'build\EditorKokemon.exe'

# Recompilar si falta o si algun archivo del editor es mas nuevo que el programa.
$ultimo = (Get-ChildItem $fuente -File | Measure-Object LastWriteTime -Maximum).Maximum
if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $ultimo) {
    Write-Host 'Compilando el editor...'
    New-Item -ItemType Directory -Force (Join-Path $destino 'herramientas\EditorKokemon'), (Join-Path $destino 'instalador\archivos') | Out-Null
    Copy-Item (Join-Path $fuente '*') (Join-Path $destino 'herramientas\EditorKokemon') -Recurse -Force -Exclude 'bin', 'obj', 'build'
    Copy-Item (Join-Path $raiz 'instalador\archivos\icono.ico') (Join-Path $destino 'instalador\archivos') -Force
    dotnet build (Join-Path $destino 'herramientas\EditorKokemon\EditorKokemon.csproj') -c Release -v q -nologo -o (Join-Path $destino 'build')
    if ($LASTEXITCODE -ne 0) { Read-Host 'No se pudo compilar el editor (falta el .NET SDK 9?). Enter para cerrar'; exit 1 }
}

# Exportados que subieron los jugadores ("MR2 Subir exportados"): issues abiertos con la etiqueta "exportado".
# Se bajan a recibidos\ ("#numero titulo.json"); los de issues ya cerrados (incorporados o descartados) se borran.
$recibidos = Join-Path $raiz 'recibidos'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $issues = @(Invoke-RestMethod -Uri 'https://api.github.com/repos/tms87/mr2-rancho-de-kokemones/issues?labels=exportado&state=open&per_page=100' `
            -Headers @{ 'User-Agent' = 'MR2-Rancho-de-Kokemones'; Accept = 'application/vnd.github+json' } -TimeoutSec 20)
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
            Write-Host "Recibido: $([IO.Path]::GetFileName($archivo))"
        }
    }
    Get-ChildItem $recibidos -Filter '#*.json' | Where-Object { $abiertos -notcontains $_.FullName } | Remove-Item -Force
}
catch {
    Write-Host "No se pudieron bajar los exportados recibidos: $($_.Exception.Message)" -ForegroundColor Yellow
}

Start-Process $exe -ArgumentList "`"$raiz`""
