# Design system

An original visual language for a lab instrument: flat surfaces, one hairline border, the brand palette used for *meaning*
rather than decoration, and a legible monospace for sequences. Everything lives in `DiseaseMutationsApp/wwwroot/css`:
`tokens.css` (variables), `base.css` (Bootstrap bindings, typography, forms, buttons), `components.css`.

## Palette semantics

| Family | Hex | Means |
|---|---|---|
| Pink `#F6D8F1 / #E68DD8 / #C55CB4 / #8E2E7E` | variant / **mutated** |
| Mint `#D5F9E6 / #A1EEC6 / #5FBE8E / #1E5B3F` | reference / **original** |
| Crimson `#B31237 / #600B1E` | **critical state only**: errors, destructive actions, off-target risk |

Ordinary text and links never use crimson. Text-on-accent is the near-black `#1D0F1A` (contrast against `#E68DD8` is
about 7:1); body text `#2A1A26` on `#FFF9FD`; muted `#6B5565` (>= 6:1).

## Tokens

Surfaces `--bg --surface --surface-sunken --surface-hover --border --border-strong --divider`; text `--text-primary
--text-secondary --text-muted --text-on-accent`; roles `--variant* --reference* --critical*`; state
`--state-info|success|warning|danger` (+ `-bg`, `-border`); `--focus-ring`; elevation `--elev-0|1|2` (only the sticky app bar
and the error toast float); type `--font-sans --font-mono --text-xs..3xl`, weights and tracking; spacing `--space-1..7`;
radii `--radius-sm|md|lg`; motion `--transition` (only colour, background-colour, border-colour, opacity, transform).

Type: IBM Plex Sans (400/600) and JetBrains Mono (400/600), self-hosted (SIL OFL 1.1, `wwwroot/fonts`, no CDN). The
monospace disambiguates `0/O` and `1/l`, which matters for sequences.

## Dark theme

`[data-bs-theme="dark"]` redefines the same tokens, desaturating the hues rather than inverting them: ground `#1A1218`,
pink `#D9A4CF`, mint `#8FD9B4`, critical `#FF8FA6`. The theme is chosen from the OS preference and persisted by the
app-bar toggle (`localStorage["grna.theme"]`), applied before first paint to avoid a flash.

## Icons

`wwwroot/icons.svg` is an inline-able sprite of 29 stroke icons (`settings download link copy check alert info grid table
chart flask dna sort sort-up sort-down close play stop chevron external plus trash sun moon retry send list pulse`).
`<Icon Name="download" />` renders `<svg aria-hidden><use href="icons.svg#download"/></svg>`; give it a `Label` to make it an
`img` with a title. Icons use `currentColor`, so they theme for free. There is no emoji in user-facing markup.

## Components

- **App shell** (`MainLayout`, `NavMenu`): sidebar with a 2 px left rail on the active item, collapsing to an icon rail
  below `lg`; app bar with run status, shortlist count and theme toggle; skip link; `ErrorBoundary` around the page body.
- **TabStrip<T>**: `tablist/tab/tabpanel`, `aria-selected`, `aria-controls`, roving `tabindex`, Left/Right/Home/End.
  Only the active panel is rendered.
- **TabStatusLabel**: state as a distinct *shape* (ring spinner, dotted ring, filled dot, triangle, square) plus text plus
  `aria-label`: never colour alone. Long labels truncate in CSS with a `title`.
- **SequenceView / SequenceSpan**: structured, escaped output (no `MarkupString`): rows of 60 nt in blocks of 10, 1-based
  coordinates, mutation and seed highlighting, collapse beyond 6 rows, copy-as-FASTA.
- **GrnaResultsTable**: sortable headers are `<button>`s inside `<th aria-sort>`; right-aligned tabular numerals; inline
  bars behind Score and GC; filter row (min score, max alignments, GC range, sequence substring) and page size; alignment
  **risk chips** with text and glyph (`0 none`, `1 unique`, `2-3 caution`, `4-5 high`, `6+ saturated`: the count saturates at
  Bowtie's `-k 6`).
- **PoolPlateMap**: CSS-grid plate with a roving tab stop (arrow keys, Home/End), a hover/focus readout panel and positive-well marking.
- **Skeletons** match the table geometry so layout does not jump. Errors: `ErrorBoundary` per variant, around the results
  region and around the page; `#blazor-error-ui` and a reconnect dialog in `App.razor`; a static `error.html` for server failures.

## Accessibility baseline

Visible `:focus-visible` ring everywhere (`--focus-ring`, 2 px, offset 2 px); contrast-checked pairs; status never by colour
alone; keyboard paths for tabs, sort headers, the plate map and every button; `prefers-reduced-motion` disables animation.

## What we do not do

No gradient-filled text, no decorative blur or glows, no hover-lift on static cards, no background "blobs", no emoji, no
colour-only state, no CDN assets. The only gradient on a page is the 2 px rail under the page title.

## Vendored assets

Bootstrap **5.3.3** (`bootstrap.min.css`, SHA256 `3c8f27e6009ccfd710a905e6dcf12d0ee3c6f2ac7da05b0572d3e0d12e736fc8`, MIT, license
header retained) provides the grid and utilities; its look is overridden by the tokens. Fonts: see `THIRD-PARTY-NOTICES.md` for versions and checksums.
