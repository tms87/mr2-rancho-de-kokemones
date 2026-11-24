# Configura tu clave para "MR2 Subir exportados": pegas la clave que te pasaron por privado (un token de GitHub que
# solo permite crear issues en el repositorio de tu liga). Se guarda cifrada con tu usuario de Windows
# (clave-issues.dat, junto a este programa): copiada a otra PC no sirve. Se puede volver a correr para cambiarla.
param([string]$Destino = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
# El repositorio de la liga de esta instalacion.
$repo = ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'liga-instalada.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).liga.repo
if (-not $repo) { Write-Host 'Esta instalacion usa una liga local: no hace falta clave.' -ForegroundColor Yellow; exit }
$archivo = Join-Path $Destino 'clave-issues.dat'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host 'Configurar clave - MR2 Subir exportados' -ForegroundColor Cyan
Write-Host ''

# La clave se prueba contra GitHub antes de guardarla.
while ($true) {
    $clave = (Read-Host 'Pega la clave que te pasaron (empieza con github_pat_)').Trim()
    if (-not $clave) { continue }
    try {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repo" -TimeoutSec 20 -Headers @{
            Authorization = "Bearer $clave"; 'User-Agent' = 'MR2-Rancho-de-Kokemones'; Accept = 'application/vnd.github+json'
        } | Out-Null
        break
    }
    catch {
        Write-Host "La clave no funciona ($($_.Exception.Message)). Fijate de copiarla entera, o pedi una nueva." -ForegroundColor Red
    }
}

# Se guarda cifrada (DPAPI: solo tu usuario de Windows, en esta PC, puede leerla).
New-Item -ItemType Directory -Force $Destino | Out-Null
ConvertTo-SecureString $clave -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $archivo -Encoding ASCII
Write-Host ''
Write-Host 'Listo: la clave quedo guardada. Ya podes usar "MR2 Subir exportados".' -ForegroundColor Green
