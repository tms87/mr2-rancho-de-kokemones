# Prompt para banners de torneos: colores libres (Monster Rancher 2 DX)

Variante del prompt de banners original (que fijaba dorado, crema y verde azulado): acá la IA elige la paleta según el tema, pero con **pocos colores
que combinen**, nada de logos arcoíris. Copiá el bloque de abajo, cambiando `{NOMBRE}` y `{TEMA}`.

Si el tema pide rosa, violeta o lila, cambiá en todo el bloque `MAGENTA`/`magenta` por `GREEN`/`green`,
`#FF00FF` por `#00FF00` y `255,0,255` por `0,255,0`. El editor saca cualquiera de los dos fondos.

---

```
Create a tournament title logo for a 90s Japanese monster-raising RPG (Monster Rancher 2 style).

BACKGROUND RULE (must follow, overrides everything else): the logo sits on a completely flat, solid MAGENTA
background (#FF00FF, pure 255,0,255) that fills every pixel around the logo. The background is NOT part of the
design and is NOT affected by the colour palette or the theme: no white, no grey, no blue, no dark background,
no blur, no bokeh, no vignette, no texture, no scenery, no enlarged or blurred copy of the logo behind it.

Text: "{NOMBRE}" — this exact text, spelled exactly like this, and no other text.
Theme / motif: {TEMA}

Style:
- Ornate, embossed hand-lettered title like a classic 90s JRPG logo: thick bevelled letters, a dark outline
  and a soft drop shadow.
- Behind the letters, one decorative emblem that matches the theme (a medallion, crest or badge with a
  monster silhouette or symbol), plus ribbons, leaves, flames, waves or other ornaments that fit the theme.
- Warm, painted look. Clean, crisp edges.

Colours of the LOGO (important; this does not apply to the background, which is always magenta):
- Choose a colour palette for the logo that fits the theme. You are free to pick the logo colours.
- Use a LIMITED, HARMONIOUS palette: 2 or 3 main hues plus neutrals (metallic gold/silver/bronze, cream,
  dark brown or black for outlines and shadows). Every part of the logo uses those same few hues.
- Do NOT make it rainbow-coloured or multicoloured. No rainbow gradients, no letters each in a different
  colour, no neon. Gradients only between shades of the same hue (light to dark) or into metallic tones.
- Keep good contrast so the letters are easy to read: light letters with a dark outline, or the reverse.
- Do not use magenta, pink, purple or lilac anywhere in the logo itself.

Format (very important):
- Wide horizontal logo, proportion 5:2 (for example 1200 x 480 px). It is scaled down to 480 x 192.
- Background: one flat, solid MAGENTA colour (#FF00FF) filling everything around the logo, so it can be keyed
  out. Do NOT draw a grey-and-white checkerboard. No scenery, no frame, no border, no gradient, no texture
  in the background.
- The logo fills about 95% of the width and is centered vertically; nothing is cut off at the edges.
- No watermark, no signature, no extra words, no UI elements.

Final check before answering: is every pixel outside the logo the same flat magenta #FF00FF? If not, fix it.
```

Si igual te devuelve otro fondo, respondele en el mismo chat:

```
Same logo, but replace the whole background with one flat solid magenta (#FF00FF). No blur, no copy of the logo
behind it, no texture: just plain magenta around the logo.
```

---

## Ejemplos de `{TEMA}`

Describí el motivo. Si querés orientar los colores sin fijarlos, nombrá un ambiente ("de atardecer", "de bosque",
"de invierno") en vez de colores exactos.

| Torneo | Tema sugerido |
|---|---|
| Mate Cup | a gourd of mate with a metal straw on a carved leather crest, countryside feel |
| Quebracho Cup | carved hardwood crest with an axe and red-brown wood grain, rugged and strong |
| Lagoon Cup | calm lagoon with reeds and a heron, evening light |
| Snowstorm Tourney | snowflake crest with icy wind swirls, winter feel |
| Spark Tourney | lightning bolts around a small monster silhouette, energetic |
| Dune Tourney | desert dunes and a sun emblem, hot and dry |
| Pichón Cup | little chick hatching from an egg on a round badge, cute, for beginners |

## Después de generarlo

1. Abrí la imagen en **Editor Kokemon → Banners**. Acepta PNG, JPG/JFIF y WebP (aunque tenga extensión .png).
   Saca el fondo magenta o verde y la deja en 480 × 192 con transparencia. Si viene cuadrada, recorta el
   logo y lo encaja sin deformarlo.
2. Mirá la vista sobre los cuatro fondos: si quedan restos del fondo o el logo tiene huecos (partes rosas o
   violetas que se volvieron transparentes), pedile otra a la IA, o la misma con fondo verde.
3. Guardala, asignala al torneo y subila desde la pestaña **Subir**.
