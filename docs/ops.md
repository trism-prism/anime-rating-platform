# Operations

## Deploy

```mermaid
flowchart LR
  D[Developer] -->|feature branch| GH[GitHub PR]
  GH --> CI{CI}
  CI -->|green, squash merge| M[main]
  M --> VE["Vercel<br/>root dir web"]
  M -->|Wait for CI| RW["Railway<br/>nixpacks build"]
  RW --> MG["Database.Migrate()<br/>on every start"]
  MG --> HC["Healthcheck /health"]
```

## CI

```mermaid
flowchart LR
  subgraph api ["api job"]
    A1[postgres:16 service] --> A2["dotnet test<br/>TEST_PG set"]
  end
  subgraph web ["web job (node 20)"]
    W1[npm ci] --> W2[check:contrast] --> W3["npm run build<br/>tsc, vite, prerender"]
  end
```

Runs on pull requests and pushes to `main`. `npm run lint` (oxlint) exists but is not in CI.

## Tests

```mermaid
flowchart LR
  T["api.Tests<br/>44 files, xUnit"] --> IM["InMemory<br/>everything else"]
  T --> PG["Postgres, needs TEST_PG<br/>throwaway database per run<br/>social, reviews, reports, lists,<br/>tracking, profile, deletion, retention"]
```

Covered: auth, sessions, lockout, Google, email, reviews, social, reports, lists, watch list, deletion, retention, export, import, censor, security headers, series, rankings, stats. The Postgres suites do nothing without `TEST_PG` because InMemory cannot check partial indexes, check constraints or advisory locks.

## Request edge

```mermaid
flowchart TB
  R[Request to www.gengarate.com] --> V{Vercel rewrite order}
  V -->|"/api/:path*"| RW[Railway API]
  V -->|"/series/:id, /titles/:id"| PF["preview function<br/>swaps title, description, og:image<br/>cached 1 h on a hit, 1 min on a miss"]
  V -->|"real file, e.g. /faq"| PR["prerendered page<br/>own title and og tags"]
  V -->|everything else| SPA["index.html"]
```

Crawlers do not run JavaScript, so they read the tags the function and the prerender step write. Unknown ids and API failures get the plain page.

## Configuration

Environment variables on Railway (`__` is nesting), or `dotnet user-secrets` in `api/` locally. No values are committed.

```mermaid
flowchart LR
  subgraph Required
    DB["DATABASE_URL<br/>or ConnectionStrings__Default"]
    JK["Jwt__Key (32+ chars), Jwt__Issuer"]
    FU["App__FrontendUrl<br/>email links, Google redirect"]
  end
  subgraph Mail ["Mail (first one set wins)"]
    GM["Gmail__ClientId, ClientSecret,<br/>RefreshToken, From"]
    RS["Resend__ApiKey, From"]
    DV["Development: logged to console"]
  end
  subgraph Optional
    GO["Google__ClientId, ClientSecret"]
    AD["Admin__Email, first admin"]
    CO["Cors__Origins, local only"]
  end
```

`Jwt__Issuer` should not change after launch: it signs everyone out. With no mail sender set, outside Development a send throws. First-time setup is in [DEPLOY.md](../DEPLOY.md).

## Rate limits

In memory, reset on restart. Per IP when signed out, per user otherwise.

| Where | Limit |
|---|---|
| Auth routes (not refresh or logout) | 10 / min per IP |
| Profile reads, stats, overview | 60 / min |
| Reactions, audience, lists, notifications | 120 / min |
| Avatar reads | 300 / min per IP |
| Friend requests | 20 / hour |
| Replies, bio, avatar writes, blocks | 30 / hour |
| Reports | 10 / hour |
| Export, account delete, support | 5 / hour |
| AniList list import | 1 / 10 min |
| Email code tries | 5 / 15 min, per address for reset, per account for verify and delete |
| Email send | 30 s (verify), 60 s (reset, email change, delete code) |

## Security layers

```mermaid
flowchart LR
  subgraph Browser
    CSP["CSP: script-src 'self', frame-ancestors none,<br/>connect-src 'self', object-src none"]
    HS["HSTS, nosniff, X-Frame-Options DENY,<br/>Permissions-Policy"]
  end
  subgraph Session
    TK["Memory-only access token, hashed refresh,<br/>reuse revokes, checked each request"]
    PW["12+ chars, HIBP check, lockout 10 / 5 min"]
    RA["Password or emailed code for<br/>export, email change, delete"]
  end
  subgraph Data
    AZ["One visibility rule, same 404 for hidden and missing"]
    VAL["Length caps, slur masking on write,<br/>magic-byte image check, CSV formula guard"]
    DBC["Unique and check constraints,<br/>advisory locks, concurrency tokens"]
  end
  Browser --> Session --> Data
```

Known limits: `style-src 'unsafe-inline'`, rate limits reset on restart, the slur list has documented gaps, and profile pictures are public to everyone.
