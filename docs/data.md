# Data

PostgreSQL through EF Core, 26 tables (7 are Identity's). Enums are stored as strings. Identity tables hang off `AspNetUsers`.

## Accounts

```mermaid
erDiagram
  AspNetUsers ||--o{ Session : signs-in
  AspNetUsers ||--o| Avatar : "bytes kept apart"
  AspNetUsers }o--o{ AspNetRoles : "AspNetUserRoles"
  AspNetUsers ||--o{ AspNetUserLogins : "Google"
  AspNetUsers ||--o{ NotificationPreference : sets

  AspNetUsers {
    string Id PK
    string UserName UK
    string Email
    bool EmailConfirmed
    string PasswordHash "null when Google-only"
    int AccessFailedCount
    datetime LockoutEnd
    datetime CreatedAt
    string TermsVersion
    string Bio "max 300"
    datetime DeletedAt "tombstone marker"
  }
  Session {
    guid Id PK
    string CurrentTokenHash "concurrency token"
    string PreviousTokenHash "reuse detection"
    string SecurityStamp
    datetime ExpiresAt
    datetime RevokedAt
  }
  Avatar {
    string UserId PK
    bytes Data "max 2 MB"
    datetime UpdatedAt "ETag"
  }
  NotificationPreference {
    string UserId PK
    string Type PK
    bool Enabled "no row = on"
  }
```

The other Identity tables (claims, role claims, user tokens) are unused defaults.

## Catalog

```mermaid
erDiagram
  Series ||--o{ Title : "ordered by Position"
  Title ||--o{ TitleCharacter : has
  Character ||--o{ TitleCharacter : in
  VoiceActor |o--o{ TitleCharacter : voices
  Title }o--o{ Title : "TitleRelation (AniList edges, no FK)"

  Series {
    string Name
    string Kind "Anime or Manga"
    int Popularity "max of its titles"
  }
  Title {
    int AniListId UK
    string Type
    string Format
    int Year
    int Volumes
    bool SeriesLocked
    int RatingCount
    double RatingSum
  }
```

Anime and manga never share a series. `SeriesLocked` is set by an admin move or merge, and the importer then leaves that title alone. Names are indexed with trigram GIN for search.

## Users and content

```mermaid
erDiagram
  AppUser ||--o| Avatar : "1:1, bytes kept apart"
  AppUser ||--o{ Session : signs-in
  AppUser ||--o{ Rating : writes
  Title ||--o{ Rating : receives
  Rating ||--o{ Reply : "one level"
  Rating ||--o{ Reaction : "Helpful, Unhelpful, Funny"
  AppUser ||--o{ WatchListEntry : tracks
  Title ||--o{ WatchListEntry : in
  AppUser ||--o{ CustomList : owns
  CustomList ||--o{ CustomListItem : holds
  AppUser ||--o{ Friendship : "one row per pair"
  AppUser ||--o{ UserBlock : blocks
  AppUser ||--o{ Notification : receives
  AppUser ||--o{ Report : files

  Rating {
    int Story "1-10"
    int Art "1-10"
    int Characters "1-10"
    int Enjoyment "1-10"
    double Overall "round(mean, 2)"
    string ReviewText "optional"
    string Audience "Public, Friends, OnlyMe"
    int Volume "null = whole title"
  }
  WatchListEntry {
    string Status "Planning, Watching, Completed, Dropped"
    date StartedOn
    date FinishedOn
    int RewatchCount
  }
  Friendship {
    string UserLowId
    string UserHighId
    string Status "Pending, Accepted"
  }
  Report {
    string Target "Review, Reply, Profile"
    string Reason
    string Status "Open, Dismissed, Removed"
    string Snapshot "survives deletion"
  }
```

A review is a rating with text. `CustomListItem` points at a title or a series, never both (check constraint). `Report` has no FK to its target so it outlives it.

## Rating math

```mermaid
flowchart LR
  A[Story] & B[Art] & C[Characters] & D[Enjoyment] --> O["Overall = round(mean of 4, 2)"]
  O --> T["Title average<br/>whole-title ratings only"]
  T --> S["Series average<br/>mean of rated titles"]
  T --> R["Rankings<br/>3+ ratings, audience ignored"]
```

Per-volume ratings are stored but excluded from title averages. Aggregates, rankings and "also rated" ignore audience, so an Only me rating still counts toward the totals, anonymously.

## Rating write

```mermaid
flowchart LR
  PUT(["PUT /titles/id/rating"]) --> V{"Scores 1-10<br/>text rules<br/>volume in range"}
  V -->|fail| E["400 or 403"]
  V -->|ok| TX["One transaction"]
  TX --> RT[("Ratings row")]
  TX --> W{"Whole title,<br/>not a volume?"}
  W -->|yes| TOT[("Titles.RatingCount + RatingSum<br/>retry on conflict, up to 4")]
  RT --> RK["Rankings, series average, Also rated"]
  TOT --> RK
```

Text is trimmed, capped at 2000 characters and masked by the censor. Whitespace-only text clears the review. A new rating defaults to Public, an existing one keeps its audience. Deleting a rating subtracts it from the totals and removes its Reply and Reaction notifications.

## Constraints

| Rule | Enforced by |
|---|---|
| One whole-title rating per user and title, one per user, title and volume | two partial unique indexes on `Rating` |
| One reaction per user per review | unique `(RatingId, UserId)` |
| One reaction notification per reactor and review | partial unique on `Notification` |
| One friendship row per pair, low id first | unique pair, check `UserLowId < UserHighId`, requester must be one of the two |
| No self-blocks | check `BlockerId <> BlockedId` |
| A list item is a title or a series, never both | check on `CustomListItem` |
| One open report per reporter, item and author | partial unique on `Report` where `Status = 'Open'` |
| Fuzzy search on names | trigram GIN on `Title.Name` and `Series.Name` |
| Score range 1 to 10, enum values | the API only, there is no database check |

`Notification.SubjectId` and `Report.TargetId` are plain integers with no FK, so they outlive what they point at. User FK cascades only fire on a hard delete, which the code never does. Column-level detail is in `api/Migrations/AppDbContextModelSnapshot.cs`.

## Who sees a review

```mermaid
flowchart TD
  Q[Viewer asks for content] --> AD{Admin?}
  AD -->|yes| Y[Shown]
  AD -->|no| SI{Signed in?}
  SI -->|no| PU{Public?}
  PU -->|yes| Y
  PU -->|no| N[404]
  SI -->|yes| AU{Author?}
  AU -->|yes| Y
  AU -->|no| AUD{Audience}
  AUD -->|OnlyMe| N
  AUD -->|Friends| F{Accepted friend?}
  F -->|no| N
  F -->|yes| B
  AUD -->|Public| B{Blocked either way?}
  B -->|yes| N
  B -->|no| Y
```

Hidden, blocked and missing all answer the same 404. Admins skip the rule for reviews and replies, not for lists or the feed. Replies have no audience of their own: they follow their review and are hidden only between a blocked pair.

## Account deletion

One transaction.

```mermaid
flowchart LR
  U[Delete account] --> X[Removed]
  U --> K[Kept]

  X --> X1[Friendships and blocks]
  X --> X2[Notifications, both directions]
  X --> X3[Lists, watch list, preferences]
  X --> X4[Sessions, logins, avatar]
  X --> X5[Reports about the profile]

  K --> K1["User row as tombstone<br/>name deleted-id, email and password hash nulled, locked out"]
  K --> K2["Ratings, reviews, replies, reactions<br/>shown as Deleted user"]
  K --> K3[Reports about reviews and replies, reports filed]
```

A lone admin cannot delete (409). Tombstones are never purged.

## Retention

```mermaid
flowchart LR
  S["Start + 2 min"] --> L{Lock held by another instance?}
  L -->|taken| Z[Skip]
  L -->|got it| R["Runs daily"]
  R --> A["Sessions expired or revoked"]
  R --> B["Settled reports older than 365 days"]
  R --> C["Notifications older than 90 days"]
```

Each step is isolated, so one failure does not stop the others. Pending friend requests older than 30 days are pruned when friend lists are read. Emailed codes are not stored.

## Imports

```mermaid
flowchart TD
  subgraph Titles ["Titles (admin or CLI)"]
    T1["6 pages of 50 per type, anime then manga<br/>first 10 characters per title, first Japanese voice actor"] --> T2["Up to 3 rounds of missing neighbours, cap 1500"]
    T2 --> T3["SeriesAssigner groups by PREQUEL, SEQUEL, PARENT, SIDE_STORY, SUMMARY"]
  end
  subgraph Lists ["A user's AniList list (Settings)"]
    L1["Up to 10 chunks of 500 per type"] --> L2["Match by AniList id"]
    L2 --> L3["New entries added as OnlyMe<br/>existing untouched, scores not imported"]
  end
  T3 --> DB[(Postgres)]
  L3 --> DB
```

The title import takes a Postgres advisory lock. A user's list import is guarded in memory: one per user, three at a time. AniList is called with a 700 ms pause and honors `Retry-After` on 429. Trending is cached 10 minutes with a stale copy kept for when AniList is down.

## Series grouping

```mermaid
flowchart LR
  A["Titles linked by<br/>PREQUEL, SEQUEL, PARENT,<br/>SIDE_STORY, SUMMARY<br/>same type, not locked"] --> G["Connected groups"]
  G --> E{"Group has a series?"}
  E -->|yes| RE["Reuse the lowest id"]
  E -->|no| NW["Create one"]
  RE & NW --> MV["Move titles in"]
  MV --> RF["Refresh<br/>order by release date<br/>name without Season N / Part N<br/>cover, max popularity<br/>delete empty series"]
```

Runs under its own advisory lock at the end of every title import. Admin Merge and Move set `SeriesLocked`, so later imports keep the override.
