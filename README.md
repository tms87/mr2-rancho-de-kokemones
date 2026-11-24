# Rancho de Kokemones

Mods para **Monster Rancher 2** de *Monster Rancher 1 & 2 DX* (Steam), listos para instalar.

**¿Venís a jugar?** Leé la [guía para jugadores](GUIA_JUGADORES.md): qué cambia en el juego y cómo instalarlo.

## Descargar e instalar

1. Instalá **Monster Rancher 1 & 2 DX** desde Steam.
2. Descargá **`MR2-Rancho-de-Kokemones.zip`** desde la [última versión](../../releases/latest).
3. Descomprimilo y hacé doble clic en **`Instalar.bat`**.
   - Busca el juego en Steam solo (si no lo encuentra, te pide que elijas `MF2.exe`).
   - Si falta .NET 9, lo descarga de Microsoft e instala.
   - Si faltan las librerías Visual C++ que usa el juego, las instala (desde Steam o de Microsoft).
4. Jugá con el acceso directo **"MR2 Rancho de Kokemones"** del escritorio.
5. Para leer las reglas y qué hace cada objeto, abrí la **Kokedex** (acceso directo del escritorio, se abre en el navegador). Es [instalador/archivos/Kokedex.html](instalador/archivos/Kokedex.html) y se actualiza sola junto con los rivales y torneos.

> **La primera vez** puede tardar varios minutos en abrir (se extraen los datos del juego) y no se ve nada mientras tanto. Esperá y no lo abras dos veces.
>
> **Steam tiene que estar abierto.** El acceso directo lo abre si hace falta.
>
> Si Windows muestra "Windows protegió su PC" al abrir `Instalar.bat`: *Más información* → *Ejecutar de todas formas*.

**Actualizaciones automáticas:** el acceso directo revisa GitHub cada vez que se abre el juego. Si hay una versión publicada más nueva, la baja e instala; si cambiaron rivales, torneos o imágenes en la rama `main`, baja solo esos archivos. Sin internet arranca igual. Las partidas, la configuración de los mods y los exportados no se tocan.

## Qué cambia en el juego

| Mod | Qué hace |
|---|---|
| **Discos aleatorios** | Cada disco del santuario da una raza al azar, siempre la misma para ese disco (semilla compartida: el mismo disco da lo mismo a todos). Stats, crecimiento, vida y naturaleza varían en cada creación. Incluye las especies de More Monsters, y sus discos "More Monsters" también se sortean. Las razas bloqueadas se rechazan como en el original. |
| **Reglas del rancho** | No se puede entrar a **torneos de rango inferior** al del monstruo (los libres, F, siempre; se pueden permitir N rangos abajo). El santuario crea como máximo **5 monstruos por año** y avisa cuántos quedan. **Modo ironman:** guardes donde guardes, se guarda en la ranura 15 (autoguardado). Cada año nuevo se copia el autoguardado de enero en las ranuras 1 a 7, rotando. **Competencias:** las ranuras 8 a 14 se arman solas (después del primer guardado de cada sesión) con `Competencias/competenciaNN.json`: los participantes de la competencia en el congelador y entrenador "Kokemon", para el modo VS. Si se carga una, no se guarda nada (ni manual ni autoguardado): la grabación y el autoguardado quedan como estaban. |
| **Items Kokemon** | Nuts Oil y Magic Banana bajan menos el cansancio si se usan semanas seguidas (acostumbramiento antifatiga) (se recupera al dejarlos) y como items tóxicos suman intoxicación, con una chance creciente de acortar la vida del monstruo. La Magic Banana depende de cuál de sus 3 resultados salga: sin bajar el cansancio desintoxica (resta 1 a la intoxicación); con −15 de cansancio suma a la intoxicación y al riesgo y suma 1 de acostumbramiento antifatiga, que con la bajada semanal lo deja igual (Nuts Oil y −30 suman 2); con −30, igual que una Nuts Oil. Otros objetos desintoxican (Quack Doll: entre −1 y −5 al azar; Rock Candy: 50 % −3; Irritater y Griever: 30 % −1; Half Eaten −1; Gold Peach: 50 % la mitad o 50 % todo; Silver Peach: 50 % un cuarto o 50 % la mitad, redondeado para arriba) y la vida que restan Paradoxine (3–10), Troron (1–3), Larox (0–4) y Manseitan (0–5) pasa a ser al azar, con más chance de las restas grandes cuanto más vida consumió el monstruo (promedio de 1/3 del rango de joven a 2/3 de viejo). Teromeamn, Kasseitan y Nageel ya no restan vida y suman de 1 a 5 de intoxicación; Troron suma 2 y Paradoxine 6; cada punto tira por separado el riesgo de vida. Los objetos se reconocen por nombre (tabla del juego), así que valen todas sus variantes. **Baúl de disc chips:** al empezar cada mes, los disc chips del inventario pasan a un baúl de la partida (dejan de ocupar lugar y ya no se venden); en el laboratorio la lista muestra los objetos de combinación del inventario más un chip de cada tipo del baúl, sin el límite de 20, y al combinar se descuenta del baúl. Comida del mes: 3 comidas nuevas (Squash, Chicken y Green Tea) con valores fijos más un ajuste por gusto y gustos propios por raza; el Green Tea desintoxica por constancia (racha mensual hasta 10, 15 % por punto hasta 75 %), el Chicken y el Fish ayudan a esa racha, y la Meat y el Chicken bajan el acostumbramiento antifatiga. |
| **Batallas automáticas** | En los torneos y batallas del juego tu monstruo pelea solo, con su propia inteligencia: a la pregunta de antes de la batalla, cualquier respuesta lleva al modo automático. El modo VS no cambia. |
| **Rivales Kokemon** | Cada semana, los rivales de los torneos de liga (E a S) salen sorteados de una lista por liga: los 50 originales más monstruos de jugadores (también de especies de More Monsters). El sorteo es el mismo para todos en la misma semana. Al empezar cada año (primer guardado de enero, junto con la copia anual), tu monstruo y los del congelador se exportan en un archivo "fecha Entrenador.json" con todos sus datos. Con el acceso directo **"MR2 Subir exportados"** se suben como issues del repositorio (etiqueta `exportado`; cada jugador configura una vez la clave personal que le pasan por privado) y el editor los baja para sumarlos como rivales o a competencias. FIMBA, los 4 grandes y los torneos especiales no cambian. |
| **Calendario Kokemon** | Calendario distinto cada año: los torneos chicos de A a E salen sorteados (cuántos y cuándo) de un pozo con los originales y torneos nuevos (Rookie Cup en 3 ediciones, 25 torneos nuevos con nombre, premio y bonus propios). En diciembre, semana 2, la **Champions Cup** de cada rango con rivales elegidos y bonus sorpresa. El calendario se actualiza mes a mes y muestra 7 meses hacia adelante. Los torneos se definen en `mods/CalendarioKokemon/MRDX.Custom.CalendarioKokemon/torneos.json`. |

Además el paquete incluye **More Monsters** (más de 240 especies nuevas).

Si algo falla, los registros están en `%APPDATA%\Reloaded-Mod-Loader-II\Logs`.

## Contenido del repositorio

- `mods/` — código de los seis mods propios (C#, .NET 9, x86, para Reloaded-II). La lista de rivales de las ligas está en `mods/Leagues/MRDX.Custom.Leagues/Liga/` (un `.json` por monstruo; ver su `LEEME.txt`).
- `instalador/` — `Instalar.bat`, el instalador (`instalar.ps1`), el lanzador con actualización automática (`jugar.ps1`), la versión del paquete (`version.txt`) y las plantillas de configuración de Reloaded-II.
- `actualizacion.json` — qué datos baja el lanzador desde `main` sin publicar una versión (rivales, torneos, imágenes) y qué versión mínima del paquete necesitan.
- `herramientas/publicar.ps1` — compila los mods, arma el zip y (con `-Publicar`) crea la versión en GitHub.

### Cambiar rivales o torneos

La forma fácil: **`Editor Kokemon.bat`** (en la raíz del repositorio) abre una app con cinco pestañas: **Rivales** (revisar los exportados recibidos, que se bajan de los issues al abrir el editor, o los tuyos; sumar los que quieras con una **versión** como "Campeón E" o "Definitivo" y la liga sugerida por stats; y descartar el archivo, que cierra su issue), **Torneos** (crear y editar torneos con formulario), **Banners** (sacar el fondo magenta o verde de una imagen hecha por IA y dejarla en 480×192), **Competencias** (armar las grabaciones de competencia de las ranuras 8 a 14: cada competencia tiene nombre, rango opcional, reglas en texto libre y grabaciones numeradas con sus participantes, leídos de cualquier grabación o exportado) y **Subir a GitHub** (commit y push a `main` solo del contenido). Necesita el .NET SDK 9, Git y GitHub CLI (`gh`, para cerrar issues).

**Claves para subir exportados:** por cada amigo, creá un *fine-grained token* en GitHub (solo este repositorio, permiso **Issues: Read and write**) y pasáselo por privado; "MR2 Subir exportados" se lo pide la primera vez y lo guarda cifrado en su PC. El paquete no lleva ninguna clave.

A mano: 
Subir los cambios a `main` alcanza: los jugadores los reciben la próxima vez que abren el juego. Un rival nuevo es un `.json` en `mods/Leagues/MRDX.Custom.Leagues/Liga/jugadores/`; un torneo nuevo, una entrada en `torneos.json`. Si los datos usan algo que el mod instalado no conoce, subir `paqueteMinimo` en `actualizacion.json` y publicar una versión.

### Publicar una versión

```powershell
powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0 -Publicar
```
Toma como base el zip de la última versión publicada (Reloaded-II y los mods de la comunidad no están en el repositorio) y reemplaza los mods propios, el instalador y el lanzador.

### Compilar los mods

Cada mod se compila con `dotnet build -c Release` dentro de su carpeta (`mods/<Mod>/MRDX.Custom.<Mod>`); la salida queda en `mods/<Mod>/build/`. Se compila contra `lib/MRDX.Base.Mod.Interfaces.dll` (la del MRDX Base Mod 1.2.2 instalado), no contra una copia del código fuente.

El zip de la versión es una carpeta de Reloaded-II con todos los mods instalados, la configuración de cada mod en `Reloaded-II/User/Mods/` y el contenido de `instalador/`.

## Créditos

- [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II), de Sewer56 (GPLv3).
- [MRDX Base Mod, Extract Data Bin y More Monsters](https://github.com/ranchercommunity/mrdx_reloaded), de la comunidad de Monster Rancher; More Monsters de ArchbishopDave y colaboradores. Se incluyen en el paquete solo para facilitar la instalación; todo el mérito es de sus autores.
- Las firmas de funciones del santuario y el congelador que usan los mods propios vienen del código de More Monsters.

Monster Rancher es una marca de Koei Tecmo. Este proyecto no está afiliado a Koei Tecmo.
