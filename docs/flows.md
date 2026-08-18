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

### Ratings scope

Signed-in people get a Ratings dropdown: All (default), Friends only, Only me. It sits with the filters on Browse and Rankings, and under the score on Series and Title pages. One choice drives them all, including reviews and Also rated, and it is kept in `localStorage` until you switch account or log out. The scope is part of the Browse address and snapshot, so Back never restores another scope's page.

```mermaid
flowchart LR
  D["Ratings: All / Friends only / Only me"] --> P["Browse · Rankings<br/>Series · Title<br/>reviews · Also rated"]
  P --> A["GET ...?scope=friends<br/>averages, counts, lists recomputed"]
  A --> N{"anything in scope?"}
  N -->|no| EM["'None of your friends have rated this yet.'<br/>'You haven't rated this yet.'"]
```

On a profile the dropdown is separate and local: another person's profile offers All and Friends only (what they shared with friends), your own adds Only me. It drives the overview (top titles, top genres, rating count).

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

## Review, reply, react

```mermaid
flowchart TB
  REV["Review"] --> RP["Reply<br/>verified email, 1-1000 chars, masked"] --> N1["Review author notified<br/>unless blocked"]
  REV --> RX["React: Helpful, Unhelpful, Funny<br/>one per review, not your own<br/>changing kind replaces it"] --> N2["Review author notified<br/>once per reactor"]
  REV --> RE["Report<br/>see Reports and moderation"]
```

Reviews list 10 at a time, by helpful (default) or newest. Replies are oldest first, one level deep. The reply author or the review author can delete a reply. Helpful minus Unhelpful is the review's score.

## Import your AniList list

```mermaid
flowchart TD
  S(["Import from AniList"]) --> N{"Name 2-20 of A-Z a-z 0-9 _ ?"}
  N -->|no| X1["400"]
  N -->|yes| L{"Free slot?<br/>1 per user per 10 min<br/>3 running at once"}
  L -->|no| X2["429"]
  L -->|yes| BG["202, runs in the background<br/>page polls the status"]
  BG --> M{"Title already in our catalog?"}
  M -->|no| UN["Counted as unmatched"]
  M -->|yes| EX{"Already on your list?"}
  EX -->|yes| AL["Left alone"]
  EX -->|no| NEW["Added as Only me<br/>CURRENT, PAUSED, REPEATING become Watching"]
  UN & AL & NEW --> R["Result: added, already there, unmatched"]
```

A private or unknown list stops with an error. Dates are kept only if they pass the same checks as the edit row (nothing after tomorrow, finish not before start). The status is in memory, so a restart loses it.

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

```mermaid
flowchart LR
  AD(["/admin"]) --> I["Import titles<br/>one at a time, 409 if running"]
  AD --> U["Users<br/>search, promote, demote<br/>the last admin stays"]
  AD --> R["Reports<br/>oldest first, 20 a page<br/>Dismiss, Remove, Clear profile"]
  AD --> S["Series<br/>Merge, Move a title"]
```

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
