# {{NOMBRE}}

Liga Kokemon del **Rancho de Kokemones** (Monster Rancher 2 DX con mods). / Kokemon League of **Rancho de Kokemones** (Monster Rancher 2 DX with mods).

## 🇦🇷 Español

**Jugadores:** descargá el instalador de la liga de [la última versión](../../releases/latest), descomprimilo y ejecutá `Instalar.bat`. La guía para jugadores está en [GUIA.md](GUIA.md) y la Kokedex se instala con el juego.

**Administrador:**
- `liga.json`: nombre, idioma, versión del Rancho que usa la liga (`rancho.repo` y `rancho.version`), links alternativos de las dependencias (`dependencias`) y la configuración de los mods (`configuracion`, ver `configuracion\opciones.json` del Rancho). Los jugadores reciben los cambios la próxima vez que abren el juego.
- `Editor Kokemon.bat`: rivales, torneos, banners, competencias, exportados recibidos y subida a GitHub. Necesita .NET SDK 9, git y GitHub CLI (`gh`).
- `herramientas\guia.ps1`: vuelve a generar `GUIA.md` con la configuración actual.
- Datos: `rivales\` (originales y de jugadores), `torneos\` (`torneos.json` e `imagenes\`), `competencias\` (grabaciones de las ranuras 8 a 14).
- Los jugadores suben exportados como issues con la etiqueta `exportado`, con la clave (token) que les pasás. **El repositorio tiene que ser público**: los jugadores lo leen sin credenciales.

## 🇬🇧 English

**Players:** download the league installer from [the latest release](../../releases/latest), unzip it and run `Instalar.bat`. The players guide is in [GUIA.md](GUIA.md) and the Kokedex is installed with the game.

**Admin:**
- `liga.json`: name, language, Rancho version used by the league (`rancho.repo` and `rancho.version`), alternative dependency links (`dependencias`) and the mods settings (`configuracion`, see the Rancho's `configuracion\opciones.json`). Players get the changes the next time they open the game.
- `Editor Kokemon.bat`: rivals, tournaments, banners, competitions, received exports and uploading to GitHub. Needs .NET SDK 9, git and GitHub CLI (`gh`).
- `herramientas\guia.ps1`: regenerates `GUIA.md` with the current settings.
- Data: `rivales\` (originals and players'), `torneos\` (`torneos.json` and `imagenes\`), `competencias\` (recordings for slots 8 to 14).
- Players upload exports as issues labeled `exportado`, with the key (token) you give them. **The repository must be public**: players read it without credentials.
