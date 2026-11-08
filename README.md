# GengaRate

This is a personal/portfolio project I built for fun. I figured building and deploying a live site would be a good challenge and a way to show off my design skills. All the titles on the site come from AniList.

The live site is at [www.gengarate.com](https://www.gengarate.com).

## Parts

```mermaid
flowchart LR
  B[Browser]
  subgraph Vercel
    S[React SPA]
    PF[Preview function]
  end
  subgraph Railway
    API[ASP.NET Core 8 API]
  end
  DB[(PostgreSQL)]
  AL[AniList GraphQL]
  GM[Gmail API]
  GO[Google OAuth]
  HB[Have I Been Pwned range API]

  B --> S
  B -->|"/api/*"| S
  S -->|rewrite| API
  B -->|"/series/:id  /titles/:id"| PF
  PF -->|reads| API
  API --> DB
  API -->|titles, trending, list import| AL
  API -->|codes, notices| GM
  API -->|sign-in| GO
  API -->|password check| HB
```

The browser only ever talks to the Vercel origin. `/api/*` is rewritten to Railway, so the refresh cookie is first-party.

## Repo

```text
api/            ASP.NET Core 8, EF Core, Identity, 15 migrations
api.Tests/      xUnit, InMemory by default, Postgres when TEST_PG is set
web/            React 19, react-router 7, Vite, TypeScript
  api/          Vercel function for link previews
  scripts/      contrast check, prerender, og-image
.github/        CI: api job, web job
```

## API layers

```mermaid
flowchart TB
  subgraph Edge
    RL[Rate limiter]
    JWT[JWT + session check]
    HD[Security headers]
  end
  subgraph Controllers
    AU[Auth / Google / Account]
    CA[Series / Titles / Trending / Rankings, with search]
    RV[Ratings / Reviews / Replies / Reactions]
    TR[Watch list / Custom lists / Stats / Import]
    SO[Friends / Blocks / Notifications / Feed]
    MD[Reports / Support / Admin]
  end
  subgraph Services
    SS[SessionService]
    EM[EmailSender]
    AC[AccountDeletion]
    IM[AniListImporter + SeriesAssigner]
    RU[Visibility rules + Censor]
  end
  subgraph Background
    RT[RetentionService, daily]
    IJ[ImportJob, admin-triggered]
  end
  DB[(AppDbContext, Postgres)]

  Edge --> Controllers --> Services --> DB
  Background --> Services
```

## A bunch of boring documentation

| | |
|---|---|
| [docs/data.md](docs/data.md) | Tables, ratings math, visibility, deletion, retention |
| [docs/auth.md](docs/auth.md) | Sign-in, Google, sessions, reset, delete |
| [docs/flows.md](docs/flows.md) | What a user can do, screen by screen |
| [docs/frontend.md](docs/frontend.md) | Routes, components, state, theme, tokens |
| [docs/ops.md](docs/ops.md) | Deploy, CI, config, limits, security |
| [STYLE.md](STYLE.md) | Design rules |
| [DEPLOY.md](DEPLOY.md) | First-time setup of Railway, Vercel, Gmail, Google |
| [SECURITY.md](SECURITY.md) | Reporting a vulnerability |

The route list lives in the API itself: `/docs` (Swagger UI), `/docs/v1/openapi.json`.

## How to run it locally in case you're into that ig

Needs the .NET 8 SDK, Node 20+ and PostgreSQL.

```bash
cd api && dotnet run
cd web && npm install && npm run dev
```

Load titles once from the Admin page (Import titles), or `dotnet api.dll import`.

```bash
cd api.Tests && dotnet test                # InMemory
TEST_PG="Host=localhost;Username=...;Password=..." dotnet test   # adds the Postgres suites
cd web && npm run check:contrast && npm run build
```

Settings are in [docs/ops.md](docs/ops.md#configuration). No values are committed.
