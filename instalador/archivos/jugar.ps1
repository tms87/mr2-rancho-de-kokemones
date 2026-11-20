# Lanzador de "MR2 Rancho de Kokemones": abre Steam si hace falta y arranca el juego con los mods.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

function Error-Ventana($texto) {
    [System.Windows.Forms.MessageBox]::Show($texto, 'MR2 Rancho de Kokemones', 'OK', 'Error') | Out-Null
    exit 1
}

try {
    $reloaded = Join-Path $PSScriptRoot 'Reloaded-II\Reloaded-II.exe'
    $app = Join-Path $PSScriptRoot 'Reloaded-II\Apps\mf2.exe\AppConfig.json'
    if (-not (Test-Path $reloaded) -or -not (Test-Path $app)) { Error-Ventana 'Falta la instalacion. Volve a ejecutar Instalar.bat.' }

    $juego = (Get-Content $app -Raw | ConvertFrom-Json).AppLocation
    if (-not (Test-Path $juego)) { Error-Ventana "No se encuentra el juego en:`n$juego`n`nSi lo moviste, volve a ejecutar Instalar.bat." }

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
    Error-Ventana "No se pudo iniciar el juego:`n$($_.Exception.Message)"
}
