<!-- Guía para jugadores de la liga, en formato Discord (un mensaje por sección, separadas por ---). La genera herramientas\guia.ps1 del repo de la liga. -->

# 🐾 {{liga.nombre}}: Rancho de Kokemones {{rancho.version}}
Es **Monster Rancher 2 DX** con varios mods. Este resumen explica todo lo que cambia **para el jugador**, para que nadie tenga ventaja por saber cómo funciona por dentro. El detalle de cada objeto está en la **Kokedex** (se instala con el juego).

## 💻 Instalación
1. Instalá **Monster Rancher 1 & 2 DX** desde Steam.
{{?liga.repo}}2. Descargá el instalador de la liga de acá: <https://github.com/{{liga.repo}}/releases/latest>
{{?!liga.repo}}2. Pedile el instalador de la liga al administrador.
3. Descomprimilo y hacé doble clic en **`Setup.bat`**.
 - Busca el juego solo; si no lo encuentra, te pide que elijas `MF2.exe`.
 - Si te falta algo que el juego necesita (.NET 9 o Visual C++), lo instala.
 - Baja Reloaded-II y los mods de la comunidad (unos 55 MB) de sus páginas oficiales.
 - Si Windows muestra *"Windows protegió su PC"*: **Más información** → **Ejecutar de todas formas**.

---

## ▶️ Para jugar
1. Abrí el juego **siempre** con el acceso directo **"MR2 Rancho de Kokemones"** del escritorio. Si no lo abrís así, los mods no se cargan.
2. Steam tiene que estar abierto; si no lo está, el acceso directo lo abre.
3. **La primera vez puede tardar varios minutos** en aparecer y no se ve nada mientras tanto. Esperá y no lo abras dos veces.

**Actualizaciones:** cada vez que abrís el juego con el acceso directo, se pone al día con la liga: rivales, torneos, banners, competencias, reglas, la Kokedex y la versión del Rancho. Sin internet arranca igual. Tus partidas no se tocan.

---

## 🎲 Discos del santuario
{{?DiscRandomizer.RandomizeBreed}}- **Cada disco da una raza al azar, pero siempre la misma.** El mismo disco te da la misma raza a vos y a cualquier otro jugador de la liga, así que conviene compartir qué sale de cada uno.
{{?DiscRandomizer.IncludeMoreMonsters}}- Se incluyen las **más de 240 especies nuevas de More Monsters**.
- **Cada monstruo creado sale distinto:**
 - stats iniciales: ±{{DiscRandomizer.StatVariancePercent}} %
 - crecimiento de cada stat: ±{{DiscRandomizer.GrowthVarianceSteps}} nivel(es)
 - esperanza de vida: ±{{DiscRandomizer.LifespanVariancePercent}} %
 - naturaleza: ±{{DiscRandomizer.NatureVariance}} puntos
- Si la raza está bloqueada, el disco se rechaza como en el juego original.
{{?RanchRules.ShrineLimitEnabled}}- **Máximo {{RanchRules.ShrinePerYear}} monstruos creados por año** en el santuario. El juego avisa cuántos te quedan, pero ese número se actualiza recién **cuando volvés al rancho**.

---

## 💾 Guardado (modo ironman)
- Elijas la ranura que elijas, **siempre se guarda en la ranura Auto**, así que no se puede guardar antes de algo y recargar.
{{?RanchRules.YearlyBackup}}- Cada enero se hace una copia automática en las ranuras 1 a 7, rotando. Son una red de seguridad, no para hacer trampa. **Las copias anuales son las que se usan para competir en el modo versus.**
- **Las ranuras 8 a 14 son para competencias:** tienen los participantes de cada competencia (en el congelador, con el entrenador "Kokemon"). Las carga el Administrador de la Liga Kokemon, llegan con las actualizaciones y aparecen después del primer guardado de cada sesión. En el modo VS elegís esa ranura y quedan registrados. Si cargás una para jugar, no se guarda nada (tampoco el autoguardado).

---

## 🏆 Torneos y batallas
{{?RanchRules.BlockLowerRankTournaments}}{{?!RanchRules.LowerRanksAllowed}}- **Un monstruo no puede entrar a torneos de un rango más bajo que el suyo.** Los torneos libres (F) siempre están abiertos.
{{?RanchRules.BlockLowerRankTournaments}}{{?RanchRules.LowerRanksAllowed}}- **Un monstruo puede entrar a torneos de hasta {{RanchRules.LowerRanksAllowed}} rango(s) por debajo del suyo**, no más abajo. Los torneos libres (F) siempre están abiertos.
- **Batallas automáticas:** en torneos y batallas, **tu monstruo pelea solo**, con su propia inteligencia. El modo VS (contra otra persona) no cambia.

---

## 🥜 Nuts Oil y Magic Banana
- **Bajan menos el cansancio si los das seguido:** la Nuts Oil y el resultado A de la Magic Banana suman **2 puntos** de **acostumbramiento antifatiga**; el resultado B, 1 (máximo {{ItemTolerance.MaxTolerance}}). Cada semana baja 1.
- Cada punto le quita un {{ItemTolerance.ToleranceStepPercent}} % a lo que baja el cansancio, hasta un mínimo del {{ItemTolerance.MinimumEffectPercent}} %. El estrés no cambia.
{{?ItemTolerance.SharedTolerance}}- **Es compartido:** alternar Nuts Oil y Magic Banana no sirve para esquivarlo.
- **Son items tóxicos:** suman **intoxicación**, que se acumula en toda la vida. Cada punto que sube tira por separado el riesgo de perder {{ItemTolerance.LifespanWeeksLost}} semana(s) de vida: {{ItemTolerance.LifespanChancePerItemPercent}} % por punto de intoxicación, con un tope de {{ItemTolerance.LifespanMaxChancePercent}} %.
- Los detalles (desintoxicantes, Green Tea, objetos que restan vida) están en la Kokedex.

---

## 👥 Rivales Kokemon
- En los torneos de liga (E a S), **los rivales se sortean cada semana** de una lista por liga: los 50 originales más **monstruos de jugadores de la liga**. El sorteo es **el mismo para todos** en la misma semana.
{{?Leagues.ExportOnSave}}- **Al empezar cada año, tu monstruo y los del congelador se exportan** en un archivo con la fecha del juego y tu entrenador, por ejemplo `1021-01-1 Gareth.json`.
{{?Leagues.ExportOnSave}}{{?liga.repo}}- **Para compartirlos, usá "MR2 Subir exportados"** (acceso directo en el escritorio). La primera vez te pide la clave que te pasa el administrador por privado (es personal: no la compartas).

---

## 📅 Calendario Kokemon
{{?CalendarioKokemon.ShuffleSmall}}- **El calendario cambia todos los años:** los torneos chicos de A a E se sortean, cuántos hay y en qué fecha, de un pozo con los originales y **los torneos nuevos de la liga**. El sorteo es el mismo para todos.
{{?CalendarioKokemon.ShuffleSmall}}- En el calendario **ves 7 meses hacia adelante**; los otros meses muestran lo del año anterior.
{{?!CalendarioKokemon.ShuffleSmall}}- Los torneos chicos tienen sus fechas originales, más **los torneos nuevos de la liga**.
- Los torneos oficiales y los especiales siguen como siempre.
