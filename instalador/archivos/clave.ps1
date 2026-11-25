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
if (-not $repo) { Write-Host (T 'Esta instalacion usa una liga local: no hace falta clave.' 'This installation uses a local league: no key is needed.') -ForegroundColor Yellow; exit 1 }
$archivo = Join-Path $Destino 'clave-issues.dat'
. (Join-Path $PSScriptRoot 'clave-github.ps1')

Write-Host (T 'Configurar clave - MR2 Subir exportados' 'Set up key - MR2 Subir exportados') -ForegroundColor Cyan
Write-Host ''

# La clave se prueba contra GitHub antes de guardarla (que pueda crear issues en la liga). Enter sin nada: cancelar.
while ($true) {
    $clave = (Read-Host (T 'Pega la clave que te pasaron (empieza con github_pat_; Enter = cancelar)' 'Paste the key you were given (it starts with github_pat_; Enter = cancel)')).Trim()
    if (-not $clave) { Write-Host (T 'No se cambio la clave.' 'The key was not changed.') -ForegroundColor Yellow; exit 1 }
    $prueba = Probar-Clave $repo $clave
    if ($prueba.Estado -eq 'ok') { break }
    Write-Host (Explicar-Clave $prueba.Estado $repo) -ForegroundColor Red
    if ($prueba.Estado -eq 'red' -and (Read-Host (T 'Guardarla igual? (s/n)' 'Save it anyway? (y/n)')) -match '^(s|si|y|yes)$') { break }
}

# Se guarda cifrada (DPAPI: solo tu usuario de Windows, en esta PC, puede leerla).
New-Item -ItemType Directory -Force $Destino | Out-Null
ConvertTo-SecureString $clave -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $archivo -Encoding ASCII
Write-Host ''
Write-Host (T 'Listo: la clave quedo guardada. Ya podes usar "MR2 Subir exportados".' 'Done: the key was saved. You can now use "MR2 Subir exportados".') -ForegroundColor Green
if ($prueba.Vence) {
    Write-Host (T "Vence el $($prueba.Vence.ToString('dd/MM/yyyy')). Despues vas a necesitar una nueva." "It expires on $($prueba.Vence.ToString('yyyy-MM-dd')). After that you will need a new one.")
}
exit 0
