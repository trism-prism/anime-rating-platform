# Style guide

To change the look, edit the token block at the top of `web/src/index.css` (every color, space, size and duration is a custom property there); to undo the redesign, revert the PR.

Built with the unslop method by Damir Sokolovsky (https://heydamir.design/unslop, licensed CC BY 4.0). This guide and the CSS adapt it.

## Audit

Before the redesign, against unslop's list of signs. Where a sign was not present it says so.

- Purple or blue-violet accent: `--accent: #6c8cff` (periwinkle) on the brand, links, avatar, bars, active filter, focus (`index.css` root, `.nav .brand`, `.link`, `.bar-fill`).
- Gradients, glows, colored shadows, blobs: none present.
- Neon or colored borders: hover on every button and card turned the border accent (`button:hover`, `.card:hover`).
- Many tints of one hue for text: five greys and blues for text (`#c4c8d4` synopsis, `--muted`, `#8d93a3`, inherited text); now three roles.
- Heavy headings: h1 at the browser default of bold 700 and `.brand` at 700 (`h1`, `.nav .brand`, `.avatar`).
- Uppercase tracked labels: none present.
- Mixed type sizes: sizes in rem and px mixed (0.7, 0.8, 0.85, 0.9, 1.2rem); system-ui font nobody chose.
- Numbers: no tabular figures, scores in the same face as prose (`.score-value`, `.entry-score`).
- Emoji: none in the interface. One icon set (inline 1.5 stroke svg) but in 16 and 18 px (`NotificationBell.tsx`, `PasswordInput.tsx`).
- Spacing: values such as 6, 10, 14, 18, 28, 34 px in padding and gaps; controls with different heights (`index.css` throughout).
- Copy: "Loading..." and "Merge..." trailing dots, short status strings with periods ("Saved.", "Added."), "No X found." pairs (components and pages).
- Greeting headlines, hype words, unverified claims, badges like "New" or "Beta": none present.
- Components: pill tags and badges for meta (`.tag`, `.badge`), a red count pill (`.bell-badge`), poster cards with the score as plain text "Anime · - / 10" showing a dash for unrated.
- Charts: all bars the same accent, nothing leads (`Stats`, `ScoreBar`).
- Radius: 5, 6, 8, 999 px mixed; nested radii not related.
- Visuals: no decorative filler; the covers are the only imagery. Fine.
- Motion: `button, a` transition on filter and transform, `filter: brightness(1.15)` on every hover, `transform: scale(0.97)` on every link press; no named property list, no menu entrance.
- Template look: pure system font, dark blue-grey page, one saturated blue everywhere.

## Direction

- Product: a rating site where people score anime and manga on five parts, keep lists and see what friends watched.
- Character: calm, exact, a little dry. Words like clean or modern are not allowed.
- World: a late night watchlist. A dim room, the screen's glow on warm graphite, a paper score slip. The covers are the only loud color on the page, so the chrome stays a warm graphite and gives them the room.
- Moments: the covers themselves, and the score. A score is the thing people come for.
- Signature: the score tag. A score is always set in the number face (Space Grotesk); on a cover it sits as a small dark tag in the accent color, in the top left corner. Nothing else on the site looks like it.

## Tokens

All defined in the `:root` block of `web/src/index.css`.

Spacing, 4px scale: 4, 8, 12, 16, 20, 24, 32, 40, 48 (`--s-1` to `--s-12`). Controls are 36 tall. Page padding 32 by 24 (16 on phones). Gap inside a group 8 to 12, between groups 24, between sections 40.

Two themes share the same token names: dark "Graphite" (the default values in `:root`) and light "Paper" (overrides in `:root[data-theme='light']`). Nothing outside those two blocks may hold a colour; `npm run check:contrast` fails on a stray hex or rgb.

| Role | Token | Dark | Light |
|---|---|---|---|
| Nav, tab bar | `--surface-deep` | #0f0e0d | #fffdf8 |
| Page | `--bg` | #141311 | #f6f2ea |
| Card, row, menu | `--card` | #1b1a17 | #fffdf8 |
| Input, button, active row | `--raised` | #262420 | #ebe5d8 |
| Hover on raised | `--raised-hover` | #2f2c27 | #e0d9ca |
| Form control edge (3:1) | `--control-border` | #7d786d | #7d7667 |
| Hairline, strong | `--hairline`, `--hairline-strong` | text at 9% and 18% | text at 12% and 24% |
| Primary text | `--text` | #ece8e1 | #1f1b16 |
| Secondary text | `--text-2` | #aca69c | #5c554a |
| Meta and captions only | `--text-muted` | #9b958a | #645d51 |
| Accent, hover, ink on it | `--accent`, `--accent-hover`, `--accent-ink` | #ff7a59, #ff8b6c, #1c0d08 | #a82d14, #8f2610, #ffffff |
| Status dot, error text | `--positive`, `--negative`, `--warning` | #4cc38a, #f2736d, #e0a458 | #1d7447, #b3261e, #965500 |
| Ink on a filled danger button | `--on-negative` | #1c0d08 | #ffffff |
| Dialog backdrop | `--scrim` | black at 60% | ink at 50% |
| Menu and popover shadow | `--shadow` | black at 40% | warm brown at 18% |
| Score tag on a cover, in a row | `--chip-bg`, `--chip-solid` | #0f0e0d at 85%, #0f0e0d | #fffdf8 at 94%, #ebe5d8 |
| Text selection | `--selection` | accent at 30% | accent at 22% |

Text roles: primary `--text`, secondary `--text-2`, muted `--text-muted` (meta and captions only). In light the nav and cards are the same warm white and the page is the cream, so surfaces separate by hairline instead of by darkness.

Accent: vermilion taken from a seal stamp (brighter in dark, deeper in light so it holds as text). It marks the main action, the focus ring, the selected filter, the leading bar of a chart, the unread count and the score tag. Nothing else. Semantic colours are only meaning (status dots, errors).

## Themes

- Choice: Light, Dark or System (default). Stored in `localStorage` key `theme` as `light` or `dark`; System removes the key. Every storage call is in try/catch; with storage blocked the choice lasts until reload and the app still works.
- Applied as `data-theme="light|dark"` on `<html>`. System resolves from `prefers-color-scheme` and follows changes live. `color-scheme` and the `theme-color` meta follow the resolved theme.
- Before first paint `public/theme-init.js` sets the attribute (an external file because the CSP is `script-src 'self'`; no inline script). After load `src/theme.ts` owns it. The two `--bg` hex values in `theme-init.js` must equal the tokens; the contrast script checks it.
- `public/theme-init.js` must be served as a real static file at `/theme-init.js`, never rewritten to `index.html` (Vercel serves files before the catch-all rewrite; a rewrite would return HTML, the script would not run and the wrong theme would flash). After each deploy fetch the live `/theme-init.js` and confirm it is the script, not HTML. First check for the theme change: recorded on PR #48 after merge.
- Controls: the Appearance card in Settings (radio group, arrow keys), "Theme: ..." in the account menu, and for signed-out visitors in the footer; the last two step through Light, Dark, System.
- Covers carry a 1px hairline outline so a pale cover keeps an edge on a pale card. The score tag on a cover has its own fill (`--chip-bg`) in both themes so its text never depends on the image.

Contrast, `npm run check:contrast` (also in CI): reads both token blocks and asserts 42 pairs per theme. Text pairs need 4.5 (text, secondary, muted on page, card, raised and hover surfaces; nav links; accent as text; primary and danger button ink; errors; the score tag). UI boundaries need 3 (control border on each surface, focus ring and selected edge, status dots, switch knob, chart bars on their track). Lowest in dark 4.7 for text and 3.5 for edges; lowest in light 4.6 for text (muted on the hover surface) and 3.6 for edges. Primary text on the page is 15.3 in light.

Type: Geist for the interface and Space Grotesk for numbers (scores, counts, positions, bars, bell badge), both from `@fontsource-variable`, bundled by Vite and served from the app, so no external request and `font-src 'self'` holds. Numbers use the `.num` class or the `--font-num` token, which set Space Grotesk with `tabular-nums`; running text stays proportional. Why: a mono face looked heavy next to prose ("0 ratings"), while Space Grotesk has clean tabular figures and a distinct zero and one. Scale 12 meta, 13 captions, 15 body, 18 section title, 28 page title. Weights 400 and 500 only. Letter spacing 0 except the page title (-0.01em).

Radii: 4 thumbnails and tags, 8 controls, 12 cards and rows, 16 dialogs. A thumbnail in a row has 8 padding inside a 12 radius, so 4.

Motion: one curve `cubic-bezier(0.22, 1, 0.36, 1)`; 150ms for hover and press, 200ms for menus, 320ms for the page entrance. Only color, background, border, opacity and transform are animated. Press scales to 0.98. All of it is off under `prefers-reduced-motion`.

## Component rules

- Buttons: a plain button is secondary (raised surface, hairline; hover steps the surface). Variants are classes: `btn-primary` (accent, one per form or section, full width in `.auth-form`), `btn-secondary`, `btn-quiet`, `btn-danger` (negative outline; add `filled` on the confirm). Disabled is a flat card surface with muted text, never reduced opacity. Settings has no primary: every section uses secondary, Delete uses danger.
- Selected state (filters, reactions you made): surface step plus an accent border.
- Links in text: primary color with an underline that turns accent on hover. Never accent text.
- Cards and rows: `--card` with a hairline, 12 radius. Poster cards step to `--raised` on hover and do not move.
- Menus and popovers: `--card`, strong hairline, soft neutral shadow, fade and rise 4px.
- Status: a 6px dot and secondary text, not a colored pill.
- Charts: bars are 4px thin; neutral by default, the accent only on the leading bar (the biggest in a group, or the Overall score).
- Icons: one set, inline svg, stroke 1.5, text-2 color; 16px in controls, 20px (`Icon.tsx`) in the navigation.
- Phones (under 640px): top bar is one row (brand, bell, avatar); Home, Trending, Rankings, My List and Friends sit in a fixed bottom tab bar (signed out: Home, Trending, Rankings, Log in). Controls are 44 high (`--control-h`). Content columns: `--w-narrow` 600 (forms, settings), `--w-reading` 800 (profile, review, list, friends, legal), `--w-wide` 1100 (browse, series, my list, admin); the footer uses the page's column.
- Focus: a 2px accent ring with 2px offset on every focusable element. Never remove it.

## Copy rules

- Sentence case. No exclamation marks in interface text.
- Short labels and buttons, no trailing period. Loading and progress text has no trailing dots ("Loading", "Posting").
- Short status messages ("Saved", "Download started") have no period. Sentences of explanation keep theirs.
- A change says what it compares to. No hype words, no unverified claims.
- Say what happened and what to do next in errors. Never blame the user.
- Data in titles (for example HAIKYU!!) is shown as it is.

## UX rules

- Loading: the area that will hold content keeps its size (`.results`, `.reviews`, `.skeleton`) so nothing jumps when data arrives. A short "Loading" line is the fallback; a skeleton block is flat, never a shimmer.
- Empty states say what is missing and where to add it ("Nothing here yet. Add titles from their pages").
- Errors appear next to what failed, in `--negative`, with `role="alert"`, and offer a retry when a retry makes sense.
- Optimistic updates only for toggles that are safe to undo (reactions); roll back and show the error if the call fails. Writes that change data others see wait for the server.
- No layout jumps: images have an aspect ratio before they load, messages reserve a line (`.admin-status`), counters use tabular figures (`.num`).
- Keyboard: every action is reachable by Tab, Enter and Space; menus close on Escape and return focus to their button. Focus is always visible.
- Forms: label above the field, hint below in muted, the error replaces the hint, the submit button shows a busy label and is disabled while the request runs, and the result (saved, failed) shows in a status line near the button.
- Destructive actions ask for confirmation in a dialog that names what will be lost.

## Revert or retune

- New palette or font: edit the token blocks (dark and light) in `web/src/index.css`, then run `npm run check:contrast`; for a font also edit the imports in `web/src/main.tsx`.
- Accent only: change `--accent`, `--accent-hover` and `--accent-ink` in both blocks.
- Whole redesign: revert the PR; no data or logic changed.
