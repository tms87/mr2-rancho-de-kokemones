# Arma un .ico estandar a partir de PNGs por tamano (icono_16.png, icono_24.png, ... icono_256.png):
# los tamanos chicos como mapa de bits de 32 bits (BMP con mascara) y 256 como PNG, que es lo que
# Windows lee en todos lados (accesos directos, explorador, barra de tareas).
#   .\crear-icono.ps1 -Carpeta <carpeta con los PNG> -Salida <archivo.ico>
param(
    [Parameter(Mandatory)][string]$Carpeta,
    [Parameter(Mandatory)][string]$Salida
)
Add-Type -AssemblyName System.Drawing

$entradas = foreach ($tam in 16, 24, 32, 48, 64, 128, 256) {
    $png = Join-Path $Carpeta "icono_$tam.png"
    if (-not (Test-Path $png)) { continue }

    if ($tam -eq 256) {
        [pscustomobject]@{ Tam = $tam; Datos = [IO.File]::ReadAllBytes($png) }
        continue
    }

    $bmp = New-Object System.Drawing.Bitmap $png
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    # BITMAPINFOHEADER (altura doble: imagen + mascara)
    $w.Write([int]40); $w.Write([int]$tam); $w.Write([int]($tam * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    # Pixeles BGRA, de abajo hacia arriba
    for ($y = $tam - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $tam; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    # Mascara AND de 1 bit (filas alineadas a 4 bytes): 1 = transparente
    $fila = [int]([math]::Ceiling($tam / 32) * 4)
    for ($y = $tam - 1; $y -ge 0; $y--) {
        $bytes = New-Object byte[] $fila
        for ($x = 0; $x -lt $tam; $x++) {
            if ($bmp.GetPixel($x, $y).A -lt 128) { $bytes[[int][math]::Floor($x / 8)] = $bytes[[int][math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $w.Write($bytes)
    }
    $bmp.Dispose()
    [pscustomobject]@{ Tam = $tam; Datos = $ms.ToArray() }
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entradas.Count)
$offset = 6 + 16 * $entradas.Count
foreach ($e in $entradas) {
    $lado = if ($e.Tam -ge 256) { 0 } else { $e.Tam }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$e.Datos.Length); $w.Write([int]$offset)
    $offset += $e.Datos.Length
}
foreach ($e in $entradas) { $w.Write($e.Datos) }
[IO.File]::WriteAllBytes($Salida, $out.ToArray())
"Icono creado: $Salida ($($entradas.Count) tamanos: $(($entradas.Tam) -join ', '))"
