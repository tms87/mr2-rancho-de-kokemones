# Configura tu clave para "MR2 Subir exportados": pegas la clave que te pasaron por privado (un token de GitHub que
# solo permite crear issues en el repositorio de tu liga). Se guarda cifrada con tu usuario de Windows
# (clave-issues.dat, junto a este programa): copiada a otra PC no sirve. Se puede volver a correr para cambiarla.
param([string]$Destino = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$Idioma = 'es'
try { $Idioma = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).idioma } catch { }
function T([string]$es, [string]$en) { if ($Idioma -eq 'en') { $en } else { $es } }
# El repositorio de la liga de esta instalacion.
$repo = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-instalada.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).liga.repo
if (-not $repo) { Write-Host (T 'Esta instalacion usa una liga local: no hace falta clave.' 'This installation uses a local league: no key is needed.') -ForegroundColor Yellow; exit }
$archivo = Join-Path $Destino 'clave-issues.dat'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host (T 'Configurar clave - MR2 Subir exportados' 'Set up key - MR2 Subir exportados') -ForegroundColor Cyan
Write-Host ''

# La clave se prueba contra GitHub antes de guardarla.
while ($true) {
    $clave = (Read-Host (T 'Pega la clave que te pasaron (empieza con github_pat_)' 'Paste the key you were given (it starts with github_pat_)')).Trim()
    if (-not $clave) { continue }
    try {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repo" -TimeoutSec 20 -Headers @{
            Authorization = "Bearer $clave"; 'User-Agent' = 'MR2-Rancho-de-Kokemones'; Accept = 'application/vnd.github+json'
        } | Out-Null
        break
    }
    catch {
        Write-Host (T "La clave no funciona ($($_.Exception.Message)). Fijate de copiarla entera, o pedi una nueva." "The key does not work ($($_.Exception.Message)). Make sure you copied all of it, or ask for a new one.") -ForegroundColor Red
    }
}

# Se guarda cifrada (DPAPI: solo tu usuario de Windows, en esta PC, puede leerla).
New-Item -ItemType Directory -Force $Destino | Out-Null
ConvertTo-SecureString $clave -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $archivo -Encoding ASCII
Write-Host ''
Write-Host (T 'Listo: la clave quedo guardada. Ya podes usar "MR2 Subir exportados".' 'Done: the key was saved. You can now use "MR2 Subir exportados".') -ForegroundColor Green
