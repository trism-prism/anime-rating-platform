# User flows

## Map

```mermaid
flowchart TD
  H["Browse /"] --> SP["Series /series/id"]
  TR[Trending] --> SP
  RK[Rankings] --> SP
  SP --> TD["Title /titles/id"]
  SP -->|"rate, add to list"| ML["My List"]
  TD -->|"rate, add to list"| ML
  SP --> RV["Review /reviews/id"]
  RV --> PF["Profile /u/name"]
  PF --> FR["Friends /feed"]
  ML --> ST[Stats]
  AV((Avatar menu)) --> PF
  AV --> SET[Settings]
  AV --> AD["Admin (admins only)"]
  BELL((Bell)) --> RV
  BELL --> FR
```

## Browse

```mermaid
flowchart LR
  Q["Search box<br/>2+ chars, 250 ms debounce"] --> URL
  F["Type, genre (12), sort"] --> URL["Query in the address<br/>replaced, not pushed"]
  URL --> API["GET /series<br/>24 per page, offset cursor"]
  API --> G[Grid of covers with score tag]
  G -->|load more| API
  G -->|open a series, then Back| SS["sessionStorage<br/>pages and scroll restored"]
  RT["Trending rail"] --> G
```

Sorts: Popular (most ratings), Top rated, Newest, A to Z. Every order ends in the id so pages never repeat. A rating bumps a version that discards the stored snapshot.

## Rate a title

```mermaid
flowchart TD
  S[Signed in?] -->|no| L[Log in]
  S -->|yes| Q["Quick rate: pick 1-10<br/>sets all four parts"]
  S --> D["Rate in detail<br/>four sliders"]
  Q --> V
  D --> V["All four set, Save enabled"]
  V --> X["Optional: review text,<br/>spoiler flag, audience"]
  X --> T{Review text?}
  T -->|yes| E{Email verified?}
  E -->|no| B["403 → verify banner"]
  E -->|yes| P["PUT /titles/id/rating"]
  T -->|no| P
  P --> OK["Averages update<br/>browse cache discarded"]
```

Manga with a known volume count also get a volume picker. Nothing is pre-filled for a title you have not rated.

## My List

```mermaid
flowchart LR
  subgraph Row
    C[Cover + title]
    ST["Status<br/>Planning, Watching,<br/>Completed, Dropped"]
    RA[Your rating + audience]
    DT["Started, finished, rewatches<br/>700 ms debounced save"]
    MN[Remove]
  end
  FL["Filter: All, status, Not on list<br/>Sort: recent, title, score"] --> Row
  Row -->|optimistic update| API["/me/watchlist"]
  NL["New list"] --> CL["Custom lists<br/>max 20 lists, 500 items"]
```

30 rows show before "Show more". Filters live in the address.

## Friends

```mermaid
stateDiagram-v2
  [*] --> Pending: send request (20/h, max 50 out)
  Pending --> Accepted: accept
  Pending --> [*]: decline or cancel
  Pending --> Accepted: reverse request auto-accepts
  Pending --> [*]: 30 days old, pruned
  Accepted --> [*]: unfriend or block
```

The feed under it lists friends' ratings and reviews, filtered by the visibility rule. Spoiler reviews show no snippet. Blocking is silent: it ends any friendship, clears notifications between the two, and the blocked user's lookups return 404.

## Notifications

```mermaid
flowchart LR
  E1[Friend request] & E2[Friend accepted] & E3[Reply to your review] & E4[Reaction to your review] & E5[Report outcome] --> N[(Notification)]
  N -->|"unless a block exists<br/>or the type is switched off"| BELL["Bell<br/>polls every 60 s while the tab is visible"]
  BELL --> A1["Accept / decline inline"]
  BELL --> A2["Open /reviews/id"]
  N -->|"90 days"| X[Deleted]
```

Per-type switches are in the avatar menu under Notification preferences. A reaction notifies once per reactor.

## Reports and moderation

```mermaid
stateDiagram-v2
  [*] --> Open: user reports review, reply or profile
  Open --> Dismissed: admin dismisses
  Open --> Removed: admin removes
  Dismissed --> [*]: purged after 365 days
  Removed --> [*]: purged after 365 days
```

| Target | Remove does |
|---|---|
| Review | strips the text, keeps the score |
| Reply | deletes it |
| Profile | clears bio and/or picture |

Removing settles every open report on the same target. The reporter gets a Report outcome notification. You can only report what you can see, not your own content, and once while a report is open. Reports need a verified email (10 an hour).

Admin page: Import titles, Users (promote, demote; last admin stays), Reports queue (oldest first), Series tools (merge, move a title).

## Settings

```mermaid
flowchart TD
  S[Settings] --> A["Account<br/>Email · Password<br/>(Google-only: a note)"]
  S --> AP["Appearance<br/>Light · Dark · System"]
  S --> I["Import and export<br/>(export hidden without a password)"]
  S --> D["Delete account (danger zone)"]
```

## Profile

```mermaid
flowchart LR
  P["/u/name"] --> O["Avatar, bio, name<br/>owner edits in place"]
  P --> OV["Overview: top 5 titles, top 3 genres"]
  P --> LS["Public lists"]
  P --> R["Ratings, 20 per page"]
  P --> M["Friend, unfriend, block, report"]
  O --> AE["Avatar editor<br/>accepts PNG, JPEG or WebP up to 2 MB, saved as 256 px WebP"]
```

Pictures are public, including to people you have blocked. A deleted account shows as "Deleted user" and its picture is gone.
