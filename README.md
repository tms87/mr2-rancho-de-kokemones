# Rancho de Kokemones

Mods para **Monster Rancher 2** de *Monster Rancher 1 & 2 DX* (Steam), listos para instalar.

## Descargar e instalar

1. Instalá **Monster Rancher 1 & 2 DX** desde Steam.
2. Descargá **`MR2-Rancho.zip`** desde la [última versión](../../releases/latest).
3. Descomprimilo y hacé doble clic en **`Instalar.bat`**.
   - Busca el juego en Steam solo (si no lo encuentra, te pide que elijas `MF2.exe`).
   - Si falta .NET 9, lo descarga de Microsoft e instala.
4. Jugá con el acceso directo **"Monster Rancher 2 (mods)"** del escritorio.

> **La primera vez** puede tardar varios minutos en abrir (se extraen los datos del juego) y no se ve nada mientras tanto. Esperá y no lo abras dos veces.
>
> **Steam tiene que estar abierto.** El acceso directo lo abre si hace falta.
>
> Si Windows muestra "Windows protegió su PC" al abrir `Instalar.bat`: *Más información* → *Ejecutar de todas formas*.

Para actualizar, descargá la versión nueva y ejecutá su `Instalar.bat`: las partidas y los datos de los mods se conservan.

## Qué cambia en el juego

| Mod | Qué hace |
|---|---|
| **Discos aleatorios** | Cada disco del santuario da una raza al azar, siempre la misma para ese disco (semilla compartida: el mismo disco da lo mismo a todos). Stats, crecimiento, vida y naturaleza varían en cada creación. Incluye las especies de More Monsters, y sus discos "More Monsters" también se sortean. Las razas bloqueadas se rechazan como en el original. |
| **Reglas del rancho** | El santuario crea como máximo **5 monstruos por año** y avisa cuántos quedan. **Modo ironman:** guardes donde guardes, se guarda en la ranura 15 (autoguardado). Cada año nuevo se copia el autoguardado de enero en las ranuras 1 a 14, rotando. |
| **Tolerancia a Nuts Oil y Magic Banana** | Pierden efecto si se usan semanas seguidas (se recupera al dejarlos) y cada uno tiene una chance creciente de acortar la vida del monstruo. |

Además el paquete incluye **More Monsters** (más de 240 especies nuevas).

Si algo falla, los registros están en `%APPDATA%\Reloaded-Mod-Loader-II\Logs`.

## Contenido del repositorio

- `mods/` — código de los tres mods propios (C#, .NET 9, x86, para Reloaded-II).
- `instalador/` — `Instalar.bat`, el instalador (`instalar.ps1`), el lanzador sin consola (`jugar.ps1`) y las plantillas de configuración de Reloaded-II.

### Compilar los mods

Cada mod se compila con `dotnet build -c Release` dentro de su carpeta (`mods/<Mod>/MRDX.Custom.<Mod>`); la salida queda en `mods/<Mod>/build/`. Se compila contra `lib/MRDX.Base.Mod.Interfaces.dll` (la del MRDX Base Mod 1.2.2 instalado), no contra una copia del código fuente.

El zip de la versión es una carpeta de Reloaded-II con todos los mods instalados, la configuración de cada mod en `Reloaded-II/User/Mods/` y el contenido de `instalador/`.

## Créditos

- [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II), de Sewer56 (GPLv3).
- [MRDX Base Mod, Extract Data Bin y More Monsters](https://github.com/ranchercommunity/mrdx_reloaded), de la comunidad de Monster Rancher; More Monsters de ArchbishopDave y colaboradores. Se incluyen en el paquete solo para facilitar la instalación; todo el mérito es de sus autores.
- Las firmas de funciones del santuario y el congelador que usan los mods propios vienen del código de More Monsters.

Monster Rancher es una marca de Koei Tecmo. Este proyecto no está afiliado a Koei Tecmo.
