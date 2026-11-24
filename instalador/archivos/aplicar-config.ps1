# Aplica la configuracion de la liga a los mods instalados (Reloaded-II\User\Mods\<mod>\Config.json).
#
#   opciones.json (del Rancho) dice que opciones tiene cada mod y de que clase es cada una:
#     liga  = la elige la liga en liga.json ("configuracion": { "<mod>": { "<opcion>": valor } }); si falta o no es
#             valida, se usa "defecto" (las semillas no tienen defecto: si faltan, no se tocan).
#     fija  = siempre "valor" (interruptores generales, pruebas, IDs internos).
#     libre = diagnostico: solo se pone "defecto" si el jugador no la tiene.
#   Las claves que no estan en opciones.json se dejan como estan.
#
# Se corre en cada arranque (el lanzador) y al instalar. Devuelve los avisos (valores invalidos de la liga).
param(
    [Parameter(Mandatory)][string]$Opciones,
    [Parameter(Mandatory)][string]$Liga,
    [Parameter(Mandatory)][string]$Reloaded
)
$ErrorActionPreference = 'Stop'

function Leer-Json($ruta) { [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) | ConvertFrom-Json }

function Valor-Valido($op, $v) {
    if ($null -eq $v) { return $false }
    switch ($op.tipo) {
        'bool' { return $v -is [bool] }
        'string' { return $v -is [string] }
        'int' {
            if (-not ($v -is [int] -or $v -is [long] -or ($v -is [double] -and [math]::Floor($v) -eq $v))) { return $false }
            if ($null -ne $op.min -and $v -lt $op.min) { return $false }
            if ($null -ne $op.max -and $v -gt $op.max) { return $false }
            return $true
        }
    }
    return $false
}

function Convertir($op, $v) { if ($op.tipo -eq 'int') { return [int]$v } else { return $v } }

$manifiesto = Leer-Json $Opciones
$datosLiga = Leer-Json $Liga
$avisos = New-Object System.Collections.Generic.List[string]

foreach ($mod in $manifiesto.mods) {
    $elegidas = $null
    if ($datosLiga.configuracion) { $elegidas = $datosLiga.configuracion.($mod.id) }

    $dir = Join-Path $Reloaded "User\Mods\$($mod.id)"
    $archivo = Join-Path $dir 'Config.json'
    # Config actual del jugador (se conservan las claves que no maneja la liga).
    $config = [ordered]@{}
    if (Test-Path $archivo) {
        try {
            $actual = Leer-Json $archivo
            foreach ($p in $actual.PSObject.Properties) { $config[$p.Name] = $p.Value }
        }
        catch { $avisos.Add("$($mod.id): Config.json ilegible, se rehace.") }
    }

    foreach ($op in $mod.opciones) {
        $clave = $op.clave
        switch ($op.clase) {
            'fija' { $config[$clave] = Convertir $op $op.valor }
            'libre' { if (-not $config.Contains($clave)) { $config[$clave] = Convertir $op $op.defecto } }
            'liga' {
                $v = $null
                $tiene = $elegidas -and ($elegidas.PSObject.Properties.Name -contains $clave)
                if ($tiene) { $v = $elegidas.$clave }
                if ($tiene -and (Valor-Valido $op $v)) { $config[$clave] = Convertir $op $v }
                elseif ($op.semilla) {
                    if ($tiene) { $avisos.Add("$($mod.id).${clave}: valor invalido ($v), no se cambia.") }
                    else { $avisos.Add("$($mod.id).${clave}: la liga no define la semilla, no se cambia.") }
                }
                else {
                    if ($tiene) { $avisos.Add("$($mod.id).${clave}: valor invalido ($v), se usa $($op.defecto).") }
                    $config[$clave] = Convertir $op $op.defecto
                }
            }
        }
    }

    New-Item -ItemType Directory -Force $dir | Out-Null
    $json = [pscustomobject]$config | ConvertTo-Json -Depth 5
    $previo = if (Test-Path $archivo) { [IO.File]::ReadAllText($archivo, [Text.Encoding]::UTF8) } else { '' }
    if ($previo.Trim() -ne $json.Trim()) { [IO.File]::WriteAllText($archivo, $json, (New-Object Text.UTF8Encoding $false)) }
}

# Claves de la liga que no existen en el Rancho (escritas a mano o de otra version).
if ($datosLiga.configuracion) {
    foreach ($m in $datosLiga.configuracion.PSObject.Properties) {
        $mod = $manifiesto.mods | Where-Object { $_.id -eq $m.Name }
        if (-not $mod) { $avisos.Add("$($m.Name): el Rancho no tiene ese mod.") ; continue }
        foreach ($p in $m.Value.PSObject.Properties) {
            $op = $mod.opciones | Where-Object { $_.clave -eq $p.Name }
            if (-not $op) { $avisos.Add("$($m.Name).$($p.Name): opcion desconocida.") }
            elseif ($op.clase -ne 'liga') { $avisos.Add("$($m.Name).$($p.Name): no la elige la liga, se ignora.") }
        }
    }
}

return $avisos
