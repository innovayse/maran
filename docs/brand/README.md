# The Maran mark

The identity, its reasoning, and every file it ships as. Unlike `docs/design/`, which mirrors the
Claude Design canvas read-only, this directory **is** the source of truth for the mark: the drawing
was made here and nowhere else.

## What it is

A **keystone** — the single wedge at the crown of an arch.

Every other stone in an arch is held up by its neighbours. The keystone is what makes the
neighbours hold: take it out and the structure comes down. That is what a control panel is to a
server, and it is a claim no rack, cloud or globe can make — those say "this is server software",
which is true of every product on the shelf and then has nothing left to say about which one.

It is also the stone at the crown of a **maran** (մառան), the Armenian word for the vaulted cellar
a household keeps its stores safe in, which is what the product is named after. The mark and the
name are the same object.

## Why it looks the way it does

Three decisions carry the drawing. All three are load-bearing; none is decoration.

### It is assembled out of pieces

Seven of them: a cap, and three courses each split on the centre line into a lit face and a face
turned away. Between the courses is **0.8 units of real air** on the 32-unit grid.

The air is air, not paint. A line painted in a background colour is invisible only against the
background it was painted for — on a photograph, a coloured header or a printed page it shows as a
stripe of the wrong colour. This is also why the one-colour cut has real gaps rather than white
lines.

Light comes from above, so each course is a step darker than the one over it, and each catches its
own highlight along its top bed.

### The joints are radial

A voussoir's bed joints converge on the **arch's own centre of curvature**. They are never
horizontal: a horizontal joint in a wedge is what a mason reads as a stone about to shear.

The stone's two sides already are radials of that arch, so the centre is simply where they meet.
Extended, they cross at **(16, 45.700)** — far below the tile. Every course is therefore bounded by
arcs concentric with that point, which is why the courses **bow upward**: each apex sits above its
own ends.

Ruled flat, this shape is a stack of boxes. Cut radial, it is part of an arch, and the viewer feels
the arch without ever being shown it.

### The light comes out from between them

The glow is painted *underneath* the courses and clipped to the whole stone, so it can only be seen
where the air is. A vault holding something alive — and equally three lit units in a tapered
chassis, which is the server reading, carried by the object's own construction rather than by an
icon bolted beside it.

A rim light runs the left silhouette, where the source would catch the edge.

## Geometry

Computed, never eyeballed. Regenerate rather than edit by hand.

| | |
|---|---|
| grid | 32 × 32 |
| shoulders | y 10, x 5.5 … 26.5 — **sharp**, the widest point |
| foot | y 27, x 10.5 … 21.5 — corners worn by 1.6 |
| cap, receding to | y 5, x 8.5 … 23.5 — rounded 2.0 |
| centre of curvature | (16, 45.700) — where the two edges meet |
| radius at shoulder | 37.212 |
| joint radii | 30.033 and 24.367 |
| air between courses | 0.8 — each course inset 0.4 from its joint |
| ridge | x 16, the centre line |
| ink extent | x 5.5 … 26.5, y 5 … 27 — centred in both axes |

The shoulders are sharp on purpose: a cut stone's widest edge is an edge.

## Colour

The panel's own tokens, from `frontend/src/assets/css/main.css`. No hue the product does not
already own. `#2E7BFF` is the `ac` token and sits at the top of the middle course's lit face;
everything else is that hue lightened for the cap or deepened for the courses below and the faces
turned away.

| Role | Values |
|---|---|
| Cap — the top face | `#8FBCFF` → `#5290FF` |
| Course 1 — lit / turned | `#3E86FF` → `#2E7BFF` / `#1B5FC4` → `#164E9E` |
| Course 2 — lit / turned | `#2E7BFF` → `#2468DC` / `#16509E` → `#123F7E` |
| Course 3 — lit / turned | `#2468DC` → `#1B55BE` / `#123F7E` → `#0C2E63` |
| Light in the joints | `#E4F0FF`, blooming to `#9BC4FF` |
| Bed highlights, rim | `#8FBCFF`, `#A8CBFF` |
| Masked-icon ground | `#0a0b0d` — the `bg` token |

Colours are written literally in the SVGs because an SVG served as a static file cannot read a CSS
custom property.

## The files

### Masters, here

| File | What it is |
|---|---|
| `maran-keystone.svg` | The full mark: seven pieces, radial joints, light between them, rim light, Gaussian bloom. |
| `maran-keystone-mono.svg` | One colour. Faces and light removed; each course a single shape, because in one colour the centre ridge has nothing to separate and drawing it would leave a hairline seam some renderers show and others do not. Takes `currentColor`. |
| `render-icons.mjs` | Regenerates every raster below. |

### Shipped, in `frontend/public/` and `website/public/`

| File | Where it is used |
|---|---|
| `favicon.svg` | The browser tab, and the site's masthead via `<img>`. **This is the master minus the Gaussian bloom** — a favicon is rasterised at 16–48px where a 0.85-unit blur is invisible, and a filter is the one feature a favicon rasteriser is most likely to drop. |
| `favicon.ico` | 16, 24, 32, 48, 64, 128, 256 in one container, for Windows, the taskbar and older browsers. |
| `icon-16/32/48/64/128/192/256/512.png` | Transparent, full-bleed. The drawing carries its own margin. |
| `apple-touch-icon.png` | 180px, **opaque and inset to 74%** — iOS crops and rounds whatever it is given, and a transparent full-bleed mark loses its corners to that crop. |
| `maskable-512.png` | **Opaque and inset to 54%** — a maskable icon is guaranteed only the central 80% circle. |

## Regenerating

After any change to `favicon.svg`:

```sh
node docs/brand/render-icons.mjs frontend
node docs/brand/render-icons.mjs website
```

Then rebuild the `.ico` for each target:

```sh
python3 -c "
from PIL import Image
for t in ('frontend', 'website'):
    Image.open(f'{t}/public/icon-256.png').convert('RGBA').save(
        f'{t}/public/favicon.ico',
        sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
"
```

Chrome does the rasterising, through the Playwright the frontend already depends on for its
end-to-end suite, so neither step adds a dependency. The rasters are checked in and are not part of
any build: regenerating them is a deliberate act, which is the point — ten PNGs exported by hand
get one of them subtly wrong, and a wrong favicon still looks like a favicon.

## Where it is wired

| Place | How |
|---|---|
| `frontend/index.html` | `<link rel="icon">` for the SVG, the 32px PNG fallback, and `apple-touch-icon`. |
| `website/app/app.vue` | The same three, as `ICON_LINKS` in the head every page carries. |
| `website/app/components/marketing/SiteHeader.vue` | `<img src="/favicon.svg">` beside the wordmark. |

`favicon.ico` is deliberately never linked: it sits at the root and the browsers that want it ask
for it by convention.

The mark is **never** inlined into a component. `rules/vue.md` and `rules/nuxt.md` forbid a
hand-written `<svg>` in a component outright, and serving one file from `public/` means the header,
the browser tab and the panel all read the same bytes — the identity cannot drift between them by
somebody editing one copy.

## What it replaced

A mark that set the letter M as live text in `Public Sans`. A browser never loads a webfont for a
static SVG, so the panel's icon was a different shape on every machine that opened it. That file
was also not well-formed XML: its comment contained `--`, which XML forbids inside a comment.

## Accessibility

The mark is decorative wherever the word "Maran" is beside it — `alt=""` plus `aria-hidden`, so a
screen reader does not announce the brand twice. Where it stands alone it takes a label instead.
Both SVGs carry `role="img"` and `aria-label="Maran"` for the standalone case.

## Known limits

- **Below about 20px the air between the courses closes** and the stone reads as one mass. That is
  the drawing degrading rather than breaking, and it is why the pieces were sized the way they are.
- **The bloom is a filter.** A renderer that drops filters loses the glow and keeps the stone. The
  favicon variant has no filter at all for exactly this reason.
- **There is no wordmark lockup yet.** The masthead sets "Maran" in the site's own type rather than
  in drawn letters. A lockup is worth having before the mark appears anywhere Innovayse does not
  control the typography.
