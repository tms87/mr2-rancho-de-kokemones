# Funciones de la clave de "MR2 Subir exportados" (las usan clave.ps1 y subir.ps1 con ". .\clave-github.ps1").

# Prueba si la clave puede crear issues en el repositorio de la liga, sin crear nada: un pedido sin titulo devuelve
# 422 si la clave tiene permiso; 401 si no es valida (mal copiada, vencida o revocada); 403 si le falta el permiso
# de issues; 404 si no tiene acceso a ese repositorio. Devuelve Estado (ok, invalida, permiso, repo, red), Vence
# (fecha de vencimiento, si GitHub la informa) y Detalle.
function Probar-Clave([string]$Repo, [string]$Clave) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $h = @{ Authorization = "Bearer $Clave"; 'User-Agent' = 'MR2-Rancho-de-Kokemones'; Accept = 'application/vnd.github+json'
            'X-GitHub-Api-Version' = '2022-11-28' }
    $codigo = 0; $vence = $null; $detalle = ''
    try {
        $r = Invoke-WebRequest -Method Post -Uri "https://api.github.com/repos/$Repo/issues" -Headers $h -Body '{}' `
            -ContentType 'application/json' -UseBasicParsing -TimeoutSec 20
        $codigo = [int]$r.StatusCode
        $vence = Leer-Vencimiento $r.Headers['github-authentication-token-expiration']
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $codigo = [int]$resp.StatusCode
            $vence = Leer-Vencimiento $resp.Headers['github-authentication-token-expiration']
        }
        $detalle = $_.Exception.Message
    }
    $estado = switch ($codigo) { 422 { 'ok' } 401 { 'invalida' } 403 { 'permiso' } 404 { 'repo' } default { 'red' } }
    [pscustomobject]@{ Estado = $estado; Codigo = $codigo; Vence = $vence; Detalle = $detalle }
}

# Estado de un error de GitHub al subir (el mismo criterio que Probar-Clave).
function Estado-Error($ErrorRecord) {
    $resp = $ErrorRecord.Exception.Response
    $codigo = if ($resp) { [int]$resp.StatusCode } else { 0 }
    switch ($codigo) { 401 { 'invalida' } 403 { 'permiso' } 404 { 'repo' } default { 'otro' } }
}

# "2026-12-24 00:00:00 UTC" -> fecha local, o $null.
function Leer-Vencimiento($Texto) {
    if (-not $Texto) { return $null }
    try {
        $utc = [datetime]::ParseExact(("$Texto" -replace '\s*UTC$', ''), 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
        return [datetime]::SpecifyKind($utc, 'Utc').ToLocalTime()
    }
    catch { return $null }
}

# Que hacer con cada problema de la clave (en el idioma de la liga: usa la funcion T del script que la carga).
function Explicar-Clave([string]$Estado, [string]$Repo) {
    switch ($Estado) {
        'invalida' { T 'La clave no es valida: puede estar mal copiada, vencida o revocada. Pedile una nueva al administrador de la liga.' 'The key is not valid: it may be mistyped, expired or revoked. Ask the league admin for a new one.' }
        'permiso'  { T "La clave no tiene permiso para crear issues en $Repo. Pedile al administrador una clave con Issues: Read and write." "The key has no permission to create issues in $Repo. Ask the admin for a key with Issues: Read and write." }
        'repo'     { T "La clave no tiene acceso al repositorio $Repo. Pedile al administrador una clave para ese repositorio." "The key has no access to the repository $Repo. Ask the admin for a key for that repository." }
        default    { T 'No se pudo comprobar la clave (sin conexion con GitHub). Proba de nuevo en un rato.' 'Could not check the key (no connection to GitHub). Try again later.' }
    }
}
