# Style guide

Status: DRAFT. The direction and palette below were chosen without review. To change the look, edit the token block at the top of `web/src/index.css` (every color, space, size and duration is a custom property there); to undo the redesign, revert the PR.

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
- Signature: the score tag. A score is always set in the mono face; on a cover it sits as a small dark tag in the accent color, in the top left corner. Nothing else on the site looks like it.

## Tokens

All defined in the `:root` block of `web/src/index.css`.

Spacing, 4px scale: 4, 8, 12, 16, 20, 24, 32, 40, 48 (`--s-1` to `--s-12`). Controls are 36 tall. Page padding 32 by 24 (16 on phones). Gap inside a group 8 to 12, between groups 24, between sections 40.

Tinted neutral ramp, warm graphite (dark only):

| Role | Token | Value |
|---|---|---|
| Nav and deepest surface | `--surface-deep` | #0f0e0d |
| Page | `--bg` | #141311 |
| Card, row, menu | `--card` | #1b1a17 |
| Input, button, active row | `--raised` | #262420 |
| Hover on raised | `--raised-hover` | #2f2c27 |
| Hairline | `--hairline`, `--hairline-strong` | text at 9% and 18% |

Text roles: primary `--text` #ece8e1, secondary `--text-2` #aca69c, muted `--text-muted` #9b958a (meta and captions only).

Accent: `--accent` #ff7a59, a vermilion taken from a seal stamp. Ink on top of it `--accent-ink` #1c0d08. It marks the main action, the focus ring, the selected filter, the leading bar of a chart, the unread count and the score tag. Nothing else. Semantic: `--positive` #4cc38a, `--negative` #f2736d, `--warning` #e0a458, only as meaning (status dots, errors).

Contrast (checked with a script, WCAG AA 4.5 needed): text 11.4 to 15.8, secondary 5.8 to 8.0, muted 4.7 to 6.5, accent on page 7.2, accent ink on accent 7.4, negative 4.9 to 6.8.

Type: Geist for the interface and Geist Mono for scores, positions and times (both from `@fontsource-variable`, served from the app, no external request). Scale 12 meta, 13 captions, 15 body, 18 section title, 28 page title. Weights 400 and 500 only. Tabular figures everywhere. Letter spacing 0 except the page title (-0.01em).

Radii: 4 thumbnails and tags, 8 controls, 12 cards and rows, 16 dialogs. A thumbnail in a row has 8 padding inside a 12 radius, so 4.

Motion: one curve `cubic-bezier(0.22, 1, 0.36, 1)`; 150ms for hover and press, 200ms for menus, 320ms for the page entrance. Only color, background, border, opacity and transform are animated. Press scales to 0.98. All of it is off under `prefers-reduced-motion`.

## Component rules

- Buttons: raised surface with a hairline; hover steps the surface. A form's submit button (`type="submit"`) is the primary action in the accent. One primary per form.
- Selected state (filters, reactions you made): surface step plus an accent border.
- Links in text: primary color with an underline that turns accent on hover. Never accent text.
- Cards and rows: `--card` with a hairline, 12 radius. Poster cards step to `--raised` on hover and do not move.
- Menus and popovers: `--card`, strong hairline, soft neutral shadow, fade and rise 4px.
- Status: a 6px dot and secondary text, not a colored pill.
- Charts: bars are 4px thin; neutral by default, the accent only on the leading bar (the biggest in a group, or the Overall score).
- Icons: one set, inline svg, 16px, stroke 1.5, text-2 color.
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
- No layout jumps: images have an aspect ratio before they load, messages reserve a line (`.admin-status`), counters use tabular figures.
- Keyboard: every action is reachable by Tab, Enter and Space; menus close on Escape and return focus to their button. Focus is always visible.
- Forms: label above the field, hint below in muted, the error replaces the hint, the submit button shows a busy label and is disabled while the request runs, and the result (saved, failed) shows in a status line near the button.
- Destructive actions ask for confirmation in a dialog that names what will be lost.

## Revert or retune

- New palette or font: edit the token block in `web/src/index.css` (and the font imports in `web/src/main.tsx`).
- Accent only: change `--accent`, `--accent-hover` and `--accent-ink`.
- Whole redesign: revert the PR; no data or logic changed.
