# Rancho de Kokemones

<h2 align="center"><a href="#español">🇦🇷 Español</a> &nbsp;·&nbsp; <a href="#english">🇬🇧 English</a></h2>

## Español

Mods para **Monster Rancher 2** de *Monster Rancher 1 & 2 DX* (Steam), pensados para jugar en grupo: cada grupo de amigos arma su propia **Liga Kokemon**, con sus rivales, torneos, competencias y reglas.

### Cómo funciona

- **Rancho de Kokemones** (este repositorio): los mods, el instalador y el lanzador, el Editor Kokemon, la Kokedex y la plantilla de las ligas. Publica versiones con los mods propios; Reloaded-II y los mods de la comunidad los baja el instalador de sus páginas oficiales (`configuracion/dependencias.json`).
- **Liga Kokemon** (un repositorio por grupo): define qué versión del Rancho usa (de este repositorio o de un fork), la configuración de los mods y sus datos: rivales, torneos nuevos, banners y competencias. Recibe los exportados de sus jugadores como issues.
- **Jugadores**: instalan con el instalador de su liga. Cada vez que abren el juego se ponen al día con la liga (datos, reglas, Kokedex y versión del Rancho).

### Crear una liga

1. Cloná este repositorio (o tu fork).
2. Ejecutá **`Crear liga.bat`** y respondé las preguntas: idioma, tipo de liga, nombre, versión del Rancho, datos iniciales (de ejemplo o vacíos) y configuración de los mods. Las semillas (raza de cada disco, sorteos de rivales y del calendario) se generan al azar para cada liga.
   - **GitHub** (recomendado): crea el repositorio de la liga en tu cuenta (te pide iniciar sesión con GitHub CLI), la etiqueta `exportado` y publica el instalador para tus jugadores. **El repositorio tiene que ser público**: los jugadores lo leen sin credenciales.
   - **Local** (no recomendada): solo una carpeta, para jugar en una PC.
3. Pasales a tus jugadores el link del instalador y la guía (`GUIA.md`, en formato Discord). Para que suban exportados, creá una clave para cada uno (el script explica cómo).

Necesita git, GitHub CLI (`gh`) para el tipo GitHub y el .NET SDK 9 para el Editor Kokemon.

### Qué cambia en el juego

| Mod | Qué hace |
|---|---|
| **Discos del santuario** | Cada disco da una raza al azar, siempre la misma en la liga; stats, crecimiento, vida y naturaleza varían en cada creación. Incluye las especies de More Monsters. |
| **Reglas del rancho** | Modo ironman (todo se guarda en el autoguardado), copias anuales en las ranuras 1 a 7, competencias en las ranuras 8 a 14, límite de creaciones por año y bloqueo de torneos de rango inferior. |
| **Items Kokemon** | Acostumbramiento antifatiga de Nuts Oil y Magic Banana, intoxicación y riesgo de vida de los items tóxicos, desintoxicantes, resta de vida al azar, baúl de disc chips y 3 comidas del mes nuevas con gustos por raza. |
| **Batallas automáticas** | En torneos y batallas el monstruo pelea solo, con su inteligencia. El modo VS no cambia. |
| **Rivales Kokemon** | Los rivales de los torneos de liga salen sorteados cada semana de la lista de la liga (los 50 originales y monstruos de jugadores). Exportación anual de tus monstruos para compartirlos. |
| **Calendario Kokemon** | Calendario sorteado cada año con los torneos nuevos de la liga (nombre, premio, bonus y banner propios). |

Los detalles, con los valores de cada liga, están en la **Kokedex** que se instala con el juego.

### Desarrollo

- `mods/`: los seis mods (C#, .NET 9, x86, Reloaded-II). Cada uno se compila con `dotnet build -c Release` en su carpeta; se compila contra `lib/MRDX.Base.Mod.Interfaces.dll` (MRDX Base Mod 1.2.2).
- `configuracion/`: opciones de los mods para las ligas (`opciones.json`), dependencias de terceros (`dependencias.json`), dónde va cada dato de la liga (`datos-liga.json`) y la plantilla de la guía.
- `instalador/`: instalador y lanzador del paquete, sincronización con la liga, subida de exportados y la plantilla de la Kokedex. `instalador-liga/`: el instalador chico que se publica en cada liga.
- `herramientas/`: `publicar.ps1`, `crear-liga.ps1` y el código del Editor Kokemon. `plantilla-liga/`: lo que se copia al crear una liga.

**Publicar una versión** (en el repositorio del remoto `origin`, así funciona igual en un fork):

```powershell
powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0 -Publicar
```

Las ligas que fijen esa versión (`rancho.version` en su `liga.json`) la instalan en el próximo arranque de cada jugador. **Fork:** una liga puede apuntar a tu fork con `rancho.repo`. Para probar el editor con tu copia local: `Editor Kokemon.bat -RanchoLocal <carpeta del Rancho>`.

## English

Mods for **Monster Rancher 2** from *Monster Rancher 1 & 2 DX* (Steam), made to play as a group: each group of friends sets up its own **Kokemon League**, with its rivals, tournaments, competitions and rules.

### How it works

- **Rancho de Kokemones** (this repository): the mods, the installer and launcher, the Kokemon Editor, the Kokedex and the league template. It publishes versions with its own mods; the installer downloads Reloaded-II and the community mods from their official pages (`configuracion/dependencias.json`).
- **Kokemon League** (one repository per group): sets which Rancho version it uses (from this repository or a fork), the mods settings and its data: rivals, new tournaments, banners and competitions. It receives its players' exports as issues.
- **Players**: install with their league's installer. Every time they open the game they sync with the league (data, rules, Kokedex and Rancho version).

### Creating a league

1. Clone this repository (or your fork).
2. Run **`Crear liga.bat`** and answer the questions: language, league type, name, Rancho version, initial data (example or empty) and mods settings. Seeds (each disc's breed, rival and calendar draws) are random for each league.
   - **GitHub** (recommended): creates the league repository in your account (it asks you to sign in with GitHub CLI), the `exportado` label and publishes the installer for your players. **The repository must be public**: players read it without credentials.
   - **Local** (not recommended): just a folder, to play on one PC.
3. Give your players the installer link and the guide (`GUIA.md`, Discord format). For them to upload exports, create a key for each one (the script explains how).

Needs git, GitHub CLI (`gh`) for the GitHub type and the .NET SDK 9 for the Kokemon Editor.

### What changes in the game

| Mod | What it does |
|---|---|
| **Shrine discs** | Each disc gives a random breed, always the same one in the league; stats, growth, lifespan and nature vary on each creation. Includes the More Monsters species. |
| **Ranch Rules** | Ironman mode (everything saves to the autosave), yearly copies in slots 1 to 7, competitions in slots 8 to 14, a yearly creation limit and blocking lower-rank tournaments. |
| **Kokemon Items** | Anti-fatigue habit for Nuts Oil and Magic Banana, toxicity and lifespan risk from toxic items, detox items, random lifespan loss, a disc chip chest and 3 new monthly foods with per-breed tastes. |
| **Auto Battles** | In tournaments and battles the monster fights by itself, with its intelligence. VS mode doesn't change. |
| **Kokemon Rivals** | League tournament rivals are drawn every week from the league's list (the 50 originals and player monsters). Yearly export of your monsters to share them. |
| **Kokemon Calendar** | A calendar drawn every year with the league's new tournaments (their own name, prize, bonus and banner). |

The details, with each league's values, are in the **Kokedex** installed with the game.

### Development

- `mods/`: the six mods (C#, .NET 9, x86, Reloaded-II). Build each with `dotnet build -c Release` in its folder; they build against `lib/MRDX.Base.Mod.Interfaces.dll` (MRDX Base Mod 1.2.2).
- `configuracion/`: mods options for leagues (`opciones.json`), third-party dependencies (`dependencias.json`), where each league data goes (`datos-liga.json`) and the guide template.
- `instalador/`: the package installer and launcher, league sync, export upload and the Kokedex template. `instalador-liga/`: the small installer published in each league.
- `herramientas/`: `publicar.ps1`, `crear-liga.ps1` and the Kokemon Editor source. `plantilla-liga/`: what gets copied when creating a league.

**Publishing a version** (to the `origin` remote's repository, so it works the same in a fork):

```powershell
powershell -ExecutionPolicy Bypass -File herramientas\publicar.ps1 -Version 1.0.0 -Publicar
```

Leagues that pin that version (`rancho.version` in their `liga.json`) install it on each player's next launch. **Fork:** a league can point to your fork with `rancho.repo`. To try the editor with your local copy: `Editor Kokemon.bat -RanchoLocal <Rancho folder>`.

## Créditos / Credits

- [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II), de/by Sewer56 (GPLv3), y sus mods Hooks, Universal Redirector y SigScan.
- [MRDX Base Mod, Extract Data Bin y More Monsters](https://github.com/ranchercommunity/mrdx_reloaded), de la comunidad de Monster Rancher / by the Monster Rancher community; More Monsters de/by ArchbishopDave y colaboradores. El instalador los baja de su página oficial / The installer downloads them from their official page.
- [7-Zip](https://www.7-zip.org) (7zr.exe), para abrir los `.7z` / to open the `.7z` files.
- Las firmas de funciones del santuario y el congelador que usan los mods propios vienen del código de More Monsters. / The shrine and freezer function signatures used by our mods come from More Monsters' code.

Monster Rancher es una marca de Koei Tecmo. Este proyecto no está afiliado a Koei Tecmo. / Monster Rancher is a trademark of Koei Tecmo. This project is not affiliated with Koei Tecmo.
