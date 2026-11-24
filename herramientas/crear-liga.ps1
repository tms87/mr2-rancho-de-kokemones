# Crea una Liga Kokemon: el repositorio de la liga (datos, configuracion, editor) y el instalador para sus jugadores.
#
#   Crear liga.bat (en la raiz del Rancho) lo ejecuta y va preguntando. Cada pregunta tambien se puede pasar como
#   parametro (para automatizar o probar): -Idioma es|en  -Tipo GitHub|Local  -Nombre  -Repo  -Privado  -Datos Ejemplo|Vacia
#   -RanchoRepo -RanchoVersion  -PorDefecto (configuracion por defecto)  -Destino  -SinPublicar (todo menos GitHub).
#
# Tipo GitHub (recomendado): crea el repositorio en tu cuenta (te pide iniciar sesion con GitHub CLI), sube la liga,
# crea la etiqueta "exportado" y publica el instalador de la liga como version del repositorio.
# Tipo Local (no recomendado): solo una carpeta; los jugadores no reciben actualizaciones por internet ni pueden subir
# exportados. Sirve para jugar en una sola PC.
param(
    [ValidateSet('', 'es', 'en')][string]$Idioma = '',
    [ValidateSet('', 'GitHub', 'Local')][string]$Tipo = '',
    [string]$Nombre,
    [string]$Repo,
    [switch]$Privado,
    [ValidateSet('', 'Ejemplo', 'Vacia')][string]$Datos = '',
    [string]$RanchoRepo,
    [string]$RanchoVersion,
    [switch]$PorDefecto,
    [string]$Destino,
    [switch]$SinPublicar
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$rancho = Split-Path $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding $false

function T([string]$es, [string]$en) { if ($script:Idioma -eq 'en') { $en } else { $es } }
function Titulo($t) { Write-Host ''; Write-Host "== $t ==" -ForegroundColor Cyan }
function Ok($t) { Write-Host "   OK  $t" -ForegroundColor Green }
function Aviso($t) { Write-Host "   !   $t" -ForegroundColor Yellow }
function Fallar($t) { Write-Host ''; Write-Host "ERROR: $t" -ForegroundColor Red; Read-Host (T 'Enter para cerrar' 'Press Enter to close') | Out-Null; exit 1 }
function Preguntar($texto, $defecto) {
    $r = Read-Host $(if ($defecto -ne $null -and "$defecto" -ne '') { "$texto [$defecto]" } else { $texto })
    if ($r.Trim() -eq '') { return $defecto } else { return $r.Trim() }
}
function Opcion($texto, [string[]]$opciones) {
    for ($i = 0; $i -lt $opciones.Count; $i++) { Write-Host ("  {0}. {1}" -f ($i + 1), $opciones[$i]) }
    while ($true) {
        $r = Preguntar $texto '1'
        if ($r -match '^\d+$' -and [int]$r -ge 1 -and [int]$r -le $opciones.Count) { return [int]$r }
    }
}
# JSON con sangria de 2 espacios (el de ConvertTo-Json de Windows PowerShell queda desalineado).
function Json-Lindo($objeto) {
    $c = $objeto | ConvertTo-Json -Depth 8 -Compress
    $sb = New-Object Text.StringBuilder; $n = 0; $enTexto = $false
    for ($i = 0; $i -lt $c.Length; $i++) {
        $ch = $c[$i]
        if ($enTexto) { [void]$sb.Append($ch); if ($ch -eq '\') { [void]$sb.Append($c[++$i]) } elseif ($ch -eq '"') { $enTexto = $false }; continue }
        switch ($ch) {
            '"' { $enTexto = $true; [void]$sb.Append($ch) }
            { $_ -eq '{' -or $_ -eq '[' } {
                $cierre = if ($ch -eq '{') { '}' } else { ']' }
                if ($c[$i + 1] -eq $cierre) { [void]$sb.Append("$ch$cierre"); $i++ }
                else { $n++; [void]$sb.Append("$ch`n" + ('  ' * $n)) }
            }
            { $_ -eq '}' -or $_ -eq ']' } { $n--; [void]$sb.Append("`n" + ('  ' * $n) + $ch) }
            ',' { [void]$sb.Append(",`n" + ('  ' * $n)) }
            ':' { [void]$sb.Append(': ') }
            default { [void]$sb.Append($ch) }
        }
    }
    return $sb.ToString() + "`n"
}function Leer-Json($ruta) { [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) | ConvertFrom-Json }
function Escribir($ruta, $texto) { [IO.File]::WriteAllText($ruta, $texto, $utf8) }
function Ejecutar([string]$programa, [string[]]$argumentos) {
    & $programa @argumentos
    if ($LASTEXITCODE -ne 0) { Fallar "$programa $($argumentos -join ' ') ($LASTEXITCODE)" }
}

Write-Host 'Rancho de Kokemones: crear una Liga Kokemon / create a Kokemon League' -ForegroundColor White

# ------------------------------------------------------------------
# 1. Idioma y tipo
# ------------------------------------------------------------------
if (-not $Idioma) {
    $Idioma = if ((Opcion 'Idioma / Language' @('Español', 'English')) -eq 2) { 'en' } else { 'es' }
}
$script:Idioma = $Idioma
if (-not $Tipo) {
    Titulo (T 'Tipo de liga' 'League type')
    $Tipo = if ((Opcion (T 'Elegí' 'Pick') @(
                    (T 'GitHub (recomendada): los jugadores instalan y se actualizan por internet, y suben sus exportados.' 'GitHub (recommended): players install and update over the internet, and upload their exports.'),
                    (T 'Local (No recomendada): solo una carpeta en esta PC, sin actualizaciones ni subida de exportados.' 'Local (Not recommended): just a folder on this PC, no updates or export uploads.'))) -eq 2) { 'Local' } else { 'GitHub' }
}
if ($Tipo -eq 'Local') { Aviso (T 'Liga local: no recomendada. Los jugadores tienen que instalar desde esta misma carpeta.' 'Local league: not recommended. Players must install from this same folder.') }

# ------------------------------------------------------------------
# 2. Herramientas y sesion de GitHub
# ------------------------------------------------------------------
Titulo (T 'Herramientas' 'Tools')
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fallar (T 'Falta git: instalalo desde https://git-scm.com y volve a empezar.' 'git is missing: install it from https://git-scm.com and start again.') }
Ok 'git'
$usuario = $null
if ($Tipo -eq 'GitHub') {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fallar (T 'Falta GitHub CLI (gh): instalalo desde https://cli.github.com (o "winget install GitHub.cli") y volve a empezar.' 'GitHub CLI (gh) is missing: install it from https://cli.github.com (or "winget install GitHub.cli") and start again.') }
    $ErrorActionPreference = 'Continue'; gh auth status 2>$null | Out-Null; $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) {
        Aviso (T 'Iniciá sesión en GitHub (se abre el navegador). Las credenciales las maneja GitHub CLI; este script no las ve.' 'Sign in to GitHub (the browser opens). GitHub CLI handles the credentials; this script never sees them.')
        gh auth login --web --git-protocol https
        if ($LASTEXITCODE -ne 0) { Fallar (T 'No se pudo iniciar sesión en GitHub.' 'Could not sign in to GitHub.') }
    }
    $usuario = (gh api user --jq .login).Trim()
    Ok "GitHub: $usuario"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Aviso (T 'Falta el .NET SDK 9: lo vas a necesitar para el Editor Kokemon (https://dotnet.microsoft.com).' 'The .NET SDK 9 is missing: you will need it for the Kokemon Editor (https://dotnet.microsoft.com).') }

# ------------------------------------------------------------------
# 3. Datos de la liga
# ------------------------------------------------------------------
Titulo (T 'La liga' 'The league')
if (-not $Nombre) { while (-not $Nombre) { $Nombre = Preguntar (T 'Nombre de la liga (ej.: Liga Kokemon de los Pibes)' 'League name (e.g. Kokemon League of the Gang)') '' } }
$slug = (($Nombre.ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', '') -replace '[^a-z0-9]+', '-').Trim('-')
if (-not $slug) { $slug = 'liga-kokemon' }
if ($Tipo -eq 'GitHub') {
    if (-not $Repo) { $Repo = Preguntar (T 'Nombre del repositorio en GitHub' 'GitHub repository name') $slug }
    if (-not $PSBoundParameters.ContainsKey('Privado')) {
        Write-Host (T '  Nota: el mecanismo actual requiere que el repositorio sea público (los jugadores lo leen sin credenciales). Con uno privado no van a poder instalar ni actualizarse.' `
                      '  Note: the current mechanism requires a public repository (players read it without credentials). With a private one they won''t be able to install or update.') -ForegroundColor Yellow
        $Privado = (Opcion (T 'Visibilidad' 'Visibility') @((T 'Público (necesario para los jugadores)' 'Public (needed by players)'), (T 'Privado' 'Private'))) -eq 2
    }
}
if (-not $Datos) {
    $Datos = if ((Opcion (T 'Datos iniciales' 'Initial data') @(
                    (T 'Ejemplo: rivales de jugadores, torneos nuevos y banners de ejemplo' 'Example: sample player rivals, new tournaments and banners'),
                    (T 'Vacía: solo los 50 rivales originales y el calendario original' 'Empty: only the 50 original rivals and the original calendar'))) -eq 2) { 'Vacia' } else { 'Ejemplo' }
}

# ------------------------------------------------------------------
# 4. Version del Rancho
# ------------------------------------------------------------------
Titulo (T 'Rancho de Kokemones' 'Rancho de Kokemones')
if (-not $RanchoRepo) {
    $ErrorActionPreference = 'Continue'; $url = (git -C $rancho remote get-url origin 2>$null); $ErrorActionPreference = 'Stop'
    $defecto = if ($url -match 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') { $Matches[1] } else { 'tms87/mr2-rancho-de-kokemones' }
    $RanchoRepo = Preguntar (T 'Repositorio del Rancho (el original o tu fork)' 'Rancho repository (the original or your fork)') $defecto
}
if (-not $RanchoVersion) {
    $ultima = $null
    try { $ultima = (Invoke-RestMethod "https://api.github.com/repos/$RanchoRepo/releases/latest" -Headers @{ 'User-Agent' = 'rancho-de-kokemones' } -TimeoutSec 15).tag_name.TrimStart('v') } catch { }
    $RanchoVersion = Preguntar (T 'Versión del Rancho que usa la liga' 'Rancho version used by the league') $ultima
}
if (-not $RanchoVersion) { Fallar (T "No hay versiones publicadas en $RanchoRepo." "There are no published versions in $RanchoRepo.") }
Ok "$RanchoRepo $RanchoVersion"

# ------------------------------------------------------------------
# 5. Configuracion de los mods
# ------------------------------------------------------------------
Titulo (T 'Configuración de los mods' 'Mods settings')
$opciones = Leer-Json (Join-Path $rancho 'configuracion\opciones.json')
if (-not $PorDefecto -and -not $PSBoundParameters.ContainsKey('PorDefecto')) {
    $PorDefecto = (Opcion (T '¿Cómo configurás los mods?' 'How do you configure the mods?') @(
            (T 'Valores recomendados (se pueden cambiar después en liga.json)' 'Recommended values (can be changed later in liga.json)'),
            (T 'Elegir cada opción' 'Pick each option'))) -eq 1
}
$configuracion = [ordered]@{}
foreach ($m in $opciones.mods) {
    $elegidas = [ordered]@{}
    $primera = $true
    foreach ($o in $m.opciones | Where-Object { $_.clase -eq 'liga' }) {
        if ($o.semilla) { $elegidas[$o.clave] = Get-Random -Minimum 1 -Maximum 2147483647; continue }
        $v = $o.defecto
        if (-not $PorDefecto) {
            if ($primera) { Write-Host ''; Write-Host "  $(if ($Idioma -eq 'en') { $m.nombre.en } else { $m.nombre.es })" -ForegroundColor White; $primera = $false }
            $nombre = if ($Idioma -eq 'en') { $o.nombre.en } else { $o.nombre.es }
            $desc = if ($Idioma -eq 'en') { $o.descripcion.en } else { $o.descripcion.es }
            Write-Host "    $desc" -ForegroundColor DarkGray
            while ($true) {
                if ($o.tipo -eq 'bool') {
                    $r = Preguntar "    $nombre (s/n)" $(if ($v) { 's' } else { 'n' })
                    if ($r -match '^(s|y|si|yes)$') { $v = $true; break }
                    if ($r -match '^(n|no)$') { $v = $false; break }
                }
                else {
                    $r = Preguntar "    $nombre ($($o.min)-$($o.max))" $v
                    if ("$r" -match '^-?\d+$' -and [int]$r -ge $o.min -and [int]$r -le $o.max) { $v = [int]$r; break }
                }
            }
        }
        $elegidas[$o.clave] = $v
    }
    if ($elegidas.Count -gt 0) { $configuracion[$m.id] = $elegidas }
}
Ok (T 'Configuración lista (las semillas son al azar, propias de esta liga).' 'Settings ready (seeds are random, unique to this league).')

# ------------------------------------------------------------------
# 6. Carpeta de la liga
# ------------------------------------------------------------------
Titulo (T 'Carpeta de la liga' 'League folder')
if (-not $Destino) { $Destino = Preguntar (T 'Dónde se crea' 'Where to create it') (Join-Path (Split-Path $rancho) $(if ($Repo) { $Repo } else { $slug })) }
if ((Test-Path $Destino) -and (Get-ChildItem $Destino -Force | Select-Object -First 1)) { Fallar (T "La carpeta $Destino ya existe y no está vacía." "The folder $Destino already exists and is not empty.") }
New-Item -ItemType Directory -Force $Destino | Out-Null
$Destino = (Resolve-Path $Destino).Path
$plantilla = Join-Path $rancho 'plantilla-liga'
robocopy $plantilla $Destino /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "robocopy $LASTEXITCODE" }

if ($Datos -eq 'Vacia') {
    Get-ChildItem (Join-Path $Destino 'rivales\jugadores') -File | Remove-Item -Force
    Get-ChildItem (Join-Path $Destino 'torneos\imagenes') -File | Remove-Item -Force
    Get-ChildItem (Join-Path $Destino 'competencias') -Filter 'competencia*.json' | Remove-Item -Force
    $tj = Join-Path $Destino 'torneos\torneos.json'
    $texto = [IO.File]::ReadAllText($tj, [Text.Encoding]::UTF8)
    $texto = [regex]::Replace($texto, '"torneos":\s*\[.*?\n  \],', '"torneos": [],', 'Singleline')
    $texto = [regex]::Replace($texto, '"excluir":\s*\[[^\]]*\]', '"excluir": []')
    Escribir $tj $texto
    # Las carpetas vacias no se suben a git.
    foreach ($d in 'rivales\jugadores', 'torneos\imagenes') { Escribir (Join-Path $Destino "$d\.gitkeep") '' }
}

$liga = [ordered]@{
    formato = 1; nombre = $Nombre; idioma = $Idioma
    rancho = [ordered]@{ repo = $RanchoRepo; version = $RanchoVersion }
    dependencias = [ordered]@{}; configuracion = $configuracion
}
Escribir (Join-Path $Destino 'liga.json') (Json-Lindo ([pscustomobject]$liga))
$readme = Join-Path $Destino 'README.md'
Escribir $readme ([IO.File]::ReadAllText($readme, [Text.Encoding]::UTF8).Replace('{{NOMBRE}}', $Nombre))
Ok $Destino

# ------------------------------------------------------------------
# 7. Repositorio (git y GitHub)
# ------------------------------------------------------------------
$repoCompleto = $null
if ($Tipo -eq 'GitHub') {
    Titulo (T 'Repositorio' 'Repository')
    Push-Location $Destino
    try {
        Ejecutar git @('init', '-q', '-b', 'main')
        if ($usuario) { $repoCompleto = "$usuario/$Repo" }
        if ($SinPublicar) { Aviso (T 'Sin publicar: se arma el repositorio local, sin crear nada en GitHub.' 'Not publishing: the local repository is built, nothing is created on GitHub.') }
        else {
            $visibilidad = if ($Privado) { '--private' } else { '--public' }
            Ejecutar gh @('repo', 'create', $repoCompleto, $visibilidad, '--description', "Liga Kokemon: $Nombre")
            Ejecutar git @('remote', 'add', 'origin', "https://github.com/$repoCompleto.git")
            $ErrorActionPreference = 'Continue'; gh label create exportado --repo $repoCompleto --color 1d76db --description 'Exportado subido por un jugador' 2>$null | Out-Null; $ErrorActionPreference = 'Stop'
        }
        # La guia con la configuracion y el link de la liga.
        & (Join-Path $Destino 'herramientas\guia.ps1') -RanchoLocal $rancho | Out-Null
        Ejecutar git @('add', '-A')
        Ejecutar git @('commit', '-q', '-m', "Liga Kokemon: $Nombre")
        if (-not $SinPublicar) { Ejecutar git @('push', '-q', '-u', 'origin', 'main'); Ok "https://github.com/$repoCompleto" }
        else { Ok (T 'Repositorio local listo.' 'Local repository ready.') }
    }
    finally { Pop-Location }
}
else {
    & (Join-Path $Destino 'herramientas\guia.ps1') -RanchoLocal $rancho | Out-Null
}

# ------------------------------------------------------------------
# 8. Instalador de la liga
# ------------------------------------------------------------------
Titulo (T 'Instalador para los jugadores' 'Installer for the players')
# La misma herramienta que la liga usa despues para republicarlo (herramientas\instalador.ps1).
$parametrosKit = @{ RanchoLocal = $rancho }
if ($repoCompleto) { $parametrosKit.Repo = $repoCompleto }
if ($SinPublicar) { $parametrosKit.SinPublicar = $true }
try { $zip = & (Join-Path $Destino 'herramientas\instalador.ps1') @parametrosKit | Select-Object -Last 1 }
catch { Fallar $_.Exception.Message }
if ($Tipo -eq 'GitHub' -and -not $SinPublicar) { Ok "https://github.com/$repoCompleto/releases/latest" } else { Ok $zip }

# ------------------------------------------------------------------
# 9. Lo que sigue
# ------------------------------------------------------------------
Titulo (T 'Listo' 'Done')
if ($Tipo -eq 'GitHub') {
    Write-Host (T @"
Para que los jugadores suban sus exportados, creá una clave (token) para cada uno:
  1. Entrá a https://github.com/settings/personal-access-tokens/new (con tu cuenta).
  2. Nombre: el del jugador. Vencimiento: el que quieras.
  3. Repository access: Only select repositories -> $repoCompleto.
  4. Permissions -> Repository permissions -> Issues: Read and write. Nada más.
  5. Pasale la clave por privado. La primera vez que use "MR2 Subir exportados" se la pide.
Una clave por jugador te deja revocar solo la de uno si hace falta.
"@ @"
For players to upload their exports, create a key (token) for each one:
  1. Go to https://github.com/settings/personal-access-tokens/new (with your account).
  2. Name: the player's. Expiration: whatever you like.
  3. Repository access: Only select repositories -> $repoCompleto.
  4. Permissions -> Repository permissions -> Issues: Read and write. Nothing else.
  5. Send them the key privately. "MR2 Subir exportados" asks for it the first time.
One key per player lets you revoke just one if needed.
"@)
}
Write-Host ''
Write-Host (T "La liga está en $Destino. Editala con 'Editor Kokemon.bat'; la guía para Discord es GUIA.md." "The league is in $Destino. Edit it with 'Editor Kokemon.bat'; the Discord guide is GUIA.md.") -ForegroundColor Green
