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

Start-Process $exe -ArgumentList "`"$raiz`""
