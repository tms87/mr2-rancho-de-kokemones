<!-- Guía para jugadores: qué cambia en el juego, en formato Discord (un mensaje por sección, separadas por ---). -->

# 🐾 Rancho de Kokemones v1.4.0
Es **Monster Rancher 2 DX** con varios mods. Este resumen explica todo lo que cambia **para el jugador**, para que nadie tenga ventaja por saber cómo funciona por dentro.

## 💻 Instalación
1. Instalá **Monster Rancher 1 & 2 DX** desde Steam.
2. Descargá **`MR2-Rancho-de-Kokemones.zip`** de acá: <https://github.com/tms87/mr2-rancho-de-kokemones/releases/latest>
3. Descomprimilo y hacé doble clic en **`Instalar.bat`**.
 - Busca el juego solo; si no lo encuentra, te pide que elijas `MF2.exe`.
 - Si te falta algo que el juego necesita (.NET 9 o Visual C++), lo instala.
 - Si Windows muestra *"Windows protegió su PC"*: **Más información** → **Ejecutar de todas formas**.

---

## ▶️ Para jugar
1. Abrí el juego **siempre** con el acceso directo **"MR2 Rancho de Kokemones"** del escritorio. Si no lo abrís así, los mods no se cargan.
2. Steam tiene que estar abierto; si no lo está, el acceso directo lo abre.
3. **La primera vez puede tardar varios minutos** en aparecer y no se ve nada mientras tanto. Esperá y no lo abras dos veces.

**Actualizaciones:** cada vez que abrís el juego con el acceso directo, se actualiza solo: rivales, torneos, banners y versiones nuevas. Sin internet arranca igual. Tus partidas no se tocan.

---

## 🎲 Discos del santuario
- **Cada disco da una raza al azar, pero siempre la misma.** El mismo disco te da la misma raza a vos y a cualquier otro jugador del grupo, así que conviene compartir qué sale de cada uno.
- Se incluyen las **más de 240 especies nuevas de More Monsters**.
- Aunque la raza sea fija, **cada monstruo creado sale distinto**:
 - stats iniciales: ±20 %
 - crecimiento de cada stat: ±1 nivel
 - esperanza de vida: ±10 %
 - naturaleza: ±30 puntos
- Si la raza está bloqueada, el disco se rechaza como en el juego original.
- **Máximo 5 monstruos creados por año** en el santuario. El juego avisa cuántos te quedan, pero ese número se actualiza recién **cuando volvés al rancho**.

---

## 💾 Guardado (modo ironman)
- Elijas la ranura que elijas, **siempre se guarda en la ranura Auto**, así que no se puede guardar antes de algo y recargar.
- Cada enero se hace una copia automática en las ranuras 1 a 7, rotando. Son una red de seguridad, no para hacer trampa.
- **Las copias anuales son las que se usan para competir en el modo versus.**
- **Las ranuras 8 a 14 son para competencias:** se arman solas con los campeones de cada competencia (en el congelador, con el entrenador "Kokemon") después del primer guardado de cada sesión. En el modo VS elegís esa ranura y quedan registrados. Si cargás una para jugar, lo que guardes queda en esa misma ranura: tu partida (Auto) no se toca.

---

## 🏆 Torneos de rango inferior
- **Un monstruo no puede entrar a torneos de un rango más bajo que el suyo.** Un monstruo B no puede ir a torneos C, D ni E.
- Los torneos libres (F) siempre están abiertos.

---

## 🥜 Nuts Oil y Magic Banana
- **Pierden efecto si los das semanas seguidas:** cada semana seguida suma un punto de tolerancia, y cada punto le quita un 10 % al efecto (100 % → 90 % → 80 %…), hasta un mínimo del 40 %.
- **La tolerancia es compartida:** alternar Nuts Oil y Magic Banana no sirve para esquivarla.
- **Se recupera** de a un punto por cada semana que no los das.
- **Pueden acortar la vida:** cada vez que el monstruo come uno, hay un riesgo de perder 1 semana de vida. El riesgo empieza en 2 % y sube 2 % por cada uno que comió en toda su vida, con un tope de 50 %.

---

## ⚔️ Batallas automáticas
- En torneos y batallas, **tu monstruo pelea solo**, con su propia inteligencia: respondas lo que respondas antes de la pelea, se pone en automático.
- El modo VS (contra otra persona) no cambia.

---

## 👥 Rivales Kokemon
- En los torneos de liga (E a S), **los rivales se sortean cada semana** de una lista por liga: los 50 originales más **monstruos de jugadores del grupo**.
- El sorteo es **el mismo para todos** en la misma semana del juego.
- **Al guardar, tu monstruo y los del congelador se exportan** en un archivo con la fecha del juego y tu entrenador (por ejemplo `1020-08-1 Gareth.json`); si nada cambió desde el último, no se repite.
- **Para compartirlos, usá "MR2 Subir exportados"** (acceso directo en el escritorio): elegís qué exportados subir y llegan al editor del grupo, donde pueden pasar a ser rivales o entrar en una competencia. La primera vez te pide la clave que te pasan por privado (es personal: no la compartas).
- **Con el paso del tiempo, los monstruos más débiles de cada rango van a bajar de rango o salir de la lista**, a medida que entren rivales más fuertes.
- FIMBA, los 4 grandes y los torneos especiales no cambian.

---

## 📅 Liga Kokemon: calendario
- **El calendario cambia todos los años.** Los torneos chicos de A a E se sortean, tanto cuántos hay como en qué fecha, de un pozo con los originales y **25 torneos nuevos**.
- Cantidad de torneos por año:
 - **A:** entre 4 y 7
 - **B:** entre 6 y 10
 - **C:** entre 7 y 11
 - **D:** entre 6 y 10
 - **E:** entre 5 y 8
- Los torneos oficiales y los especiales siguen como siempre.
- En el calendario **ves 7 meses hacia adelante**, y se va actualizando mes a mes.
- El sorteo es **el mismo para todos** en el mismo año del juego.
- **Rookie Cup** (3 ediciones por año, rango E): los rivales están **entre los más débiles** de la liga E.
- Los torneos nuevos tienen premio y objeto bonus propios, y sus rivales salen al azar de su liga.

---

## 👑 Champions Cup (diciembre, semana 2)
- Es la **liga de los campeones de cada rango**, de S a E: tu monstruo enfrenta a los campeones de ese rango, todos contra todos.
- ⚠️ **Todavía no está disponible:** se habilita cuando haya campeones.
- **Premio en dinero:** 50 % más que un torneo normal de ese rango.
- **Bonus sorpresa** (en el calendario dice *"Aleatorio"*): mitad de chance de un objeto de la lista de su rango y mitad de chance de un **disc chip**.

**Objetos posibles por rango:**
- **S:** Dragon Tusk, Fire Feather, Undine Slate, God Slate, Double-edged, Mystic Seed, Gold Peach, Silver Peach
- **A:** Taurus' Horn, Dino's Tail, Zilla Beard, Big Footstep, Gali Mask, Crystal, Gold Peach, Silver Peach
- **B:** Pure Platina, Pure Gold, Pure Silver, Fire Stone, Paradoxine, Teromeamn, Gemini's Pot, Sculpture
- **C:** Pure Gold, Pure Silver, Torles Water, Manseitan, Larox, Nageel, Gemini's Pot, Sculpture
- **D:** Pure Silver, Bay Shrimp, Shoes, Perfume, Olive Oil, Star Prune, Kasseitan, Troron, Gemini's Pot, Sculpture
- **E:** Pure Silver, Bay Shrimp, Star Prune, Incense, Fun Can, Smoked Snake, Apple Cake, Crab's Claw, Sculpture
