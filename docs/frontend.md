# Frontend

React 19, react-router 7, Vite, TypeScript. No Redux or query library: a few small stores and one fetch wrapper.

## Routes

```mermaid
flowchart LR
  O["<b>Open</b><br/>/ Browse<br/>/series/:id · /titles/:id<br/>/reviews/:id · /lists/:id<br/>/trending · /rankings<br/>/u/:username<br/>/login · /register · /forgot-password<br/>/privacy · /terms · /content-policy · /faq · /support<br/>/confirm-email-change · /auth/callback"]
  G["<b>Gated</b><br/>page redirects to /login<br/>/my-list · /stats<br/>/feed (Friends)<br/>/settings · /settings/notifications<br/>/verify"]
  A["<b>Admin</b><br/>/admin<br/>redirects home unless admin"]
  O ~~~ G ~~~ A
```

There is no router guard. Each gated page checks the user itself. `/lists` redirects to `/my-list`.

## Shell

```mermaid
flowchart TB
  AP[AuthProvider] --> LY[Layout]
  LY --> SK[Skip link]
  LY --> NV["Top bar<br/>brand · Home · Trending · Rankings · My List · Friends + badge<br/>bell · avatar menu"]
  LY --> VB[Verify-email banner]
  LY --> MA["main<br/>narrow 600 · reading 800 · wide 1100"]
  LY --> FT["Footer<br/>legal links, theme (signed out)"]
  LY --> TB["TabBar, under 640 px<br/>Home · Trending · Rankings · My List · Friends"]
```

| Column | Pages |
|---|---|
| narrow | login, register, forgot-password, verify, support, email change, auth callback |
| reading | profile, review, lists, feed, stats, legal, settings |
| wide | the rest |

## Shared components

```mermaid
flowchart LR
  subgraph Controls
    SE["Select<br/>listbox, type-ahead"]
    CD["ConfirmDialog<br/>native dialog"]
    MM["MoreMenu<br/>arrow keys"]
    IE["InlineEdit"]
    AS[AudienceSelect]
  end
  subgraph Display
    CV["Cover<br/>2:3 box, letter fallback"]
    SC[SeriesCard]
    RC["ReviewCard · ReplyThread · Spoiler"]
    SB[ScoreBar]
    AVT[Avatar]
  end
  subgraph Rating
    RF["RateForm<br/>quick + detail"]
    WD["WatchDates"]
    AL["AddToList"]
  end
  subgraph Social
    NB[NotificationBell]
    FS[FriendsSection]
    RP["ReportButton + ReportDialog"]
  end
```

## State

```mermaid
flowchart TB
  subgraph Memory
    TOK["access token"]
    PR["pendingRequests<br/>friend badge, polled 60 s,<br/>sequence number drops stale replies"]
    AVV["avatarVersion<br/>?v= to refetch a picture"]
  end
  subgraph localStorage
    AC["accounts (max 5), activeUser"]
    TH["theme (absent = System)"]
    SCP["ratings-scope (all, friends, me)<br/>reset to all on login, switch, logout"]
  end
  subgraph sessionStorage
    BS["browse-snapshot, browse-version"]
  end
  AU[auth.tsx context] --- TOK
  AU --- AC
  TS["theme.ts store"] --- TH
  SS2["scope.ts store<br/>withScope adds ?scope= unless all"] --- SCP
  BR[Browse] --- BS
  Note["Every storage call is in try/catch"]
```

## API calls

```mermaid
sequenceDiagram
  participant C as Component
  participant F as api.ts
  participant S as /api
  C->>F: api("/me/watchlist")
  F->>S: fetch with bearer token
  alt 401 on a non-auth path
    S-->>F: 401
    F->>S: POST /auth/refresh (one at a time)
    F->>S: retry once
  end
  alt not ok
    S-->>F: error body
    F-->>C: ApiError(message, status)
  else ok
    S-->>F: JSON
    F-->>C: data
  end
```

Messages come from the response's `message` or `errors[]`, else "Server error, try again" for 5xx or "Request failed (N)". A failed refresh drops that account from the stored list.

## Theme

```mermaid
sequenceDiagram
  participant P as Page load
  participant I as theme-init.js (external, CSP-safe)
  participant R as React
  P->>I: runs before first paint
  I->>I: read localStorage theme, else prefers-color-scheme
  I-->>P: set data-theme on html, meta theme-color
  R->>R: theme.ts follows OS and other tabs
```

Light, Dark or System, set from Settings, the avatar menu, or the footer when signed out. Pairs of colours are checked by `npm run check:contrast` in CI: 53 per theme, 4.5:1 for text, 3:1 for edges and the focus ring.

## Tokens

All colours are CSS variables. Dark is in `:root`, light in `:root[data-theme='light']`.

| Role | Token | Dark | Light |
|---|---|---|---|
| Page | `--bg` | `#141311` | `#f6f2ea` |
| Card | `--card` | `#1b1a17` | `#fffdf8` |
| Input, button | `--raised` | `#262420` | `#ebe5d8` |
| Text | `--text` | `#ece8e1` | `#1f1b16` |
| Accent | `--accent` | `#ff7a59` | `#a82d14` |
| Focus ring | `--focus` | `#6eb3ff` | `#0b5cad` |
| Negative | `--negative` | `#f2736d` | `#b3261e` |
| Positive | `--positive` | `#4cc38a` | `#1d7447` |
| Warning | `--warning` | `#e0a458` | `#965500` |

The accent marks the main action, the selected filter, the leading chart bar, the unread count and the score tag. Nothing else.

| | |
|---|---|
| Fonts | Geist for the interface, Space Grotesk for numbers (`.num`), self-hosted |
| Spacing | 4, 8, 12, 16, 20, 24, 32, 40, 48 |
| Radius | 4, 8, 12, 16 |
| Controls | 36 px tall, 44 px on phones |
| Motion | one curve, 150 / 200 / 320 ms, off under reduced motion |

## Breakpoints

```mermaid
flowchart LR
  A["under 640<br/>bottom TabBar,<br/>one-row top bar"] --> B["640+<br/>TabBar hidden"] --> C["900+<br/>title page two columns,<br/>rating card sticky when the viewport is 1100 px tall or more"]
```

Smaller tweaks at 399 and 359. There are also blocks for reduced motion, forced colors and no-hover devices. Full rules are in [STYLE.md](../STYLE.md).
