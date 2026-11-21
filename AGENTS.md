# GengaRate (anime-rating-platform)

Personal/portfolio anime and manga rating site, live at www.gengarate.com. Dormant: maintenance only.

## Commands
- API: `cd api && dotnet run` (.NET 8 SDK, PostgreSQL)
- Web: `cd web && npm ci && npm run dev` (Node 20+, Vite)
- Test API: `dotnet test` from the repo root (InMemory). Set `TEST_PG` to a Postgres connection string to add the `*PostgresTests` suites, as CI does.
- Check web: `cd web && npm run check:contrast && npm run build` (CI runs exactly these after `npm ci`)
- Lint web: `cd web && npm run lint` (oxlint)
- Titles: load once from the Admin page (Import titles), or `dotnet api.dll import`

## Map
| Path | What lives there |
|---|---|
| `api/` | ASP.NET Core 8 API: EF Core, Identity, migrations, controllers, services, background jobs |
| `api.Tests/` | xUnit; `TestHost.cs` builds the app; `*PostgresTests.cs` need `TEST_PG` |
| `web/` | React 19 + react-router 7 SPA; `web/api/` = Vercel link-preview function; `web/scripts/` = contrast check, prerender |
| `docs/` | auth, data, flows, frontend, ops (config keys in `docs/ops.md#configuration`) |
| `DEPLOY.md` | Railway (API + Postgres) and Vercel (web) setup |
| `STYLE.md` | Design direction; every token is in the `:root` block of `web/src/index.css` |

## Non-obvious facts
- The browser only talks to the Vercel origin: `web/vercel.json` rewrites `/api/*` to Railway so the refresh cookie stays first-party. Don't call the Railway domain from the SPA.
- Both deploy from `main`; Railway waits for CI. Never push to `main` directly; open a PR.
- No secret values are committed. Config uses ASP.NET double-underscore env names (`Jwt__Key`, `Resend__ApiKey`, ...); the API refuses to start without `Jwt__Key`.
- Mail goes through the Gmail API, with Resend as fallback (Railway blocks SMTP).
- Visual changes follow `STYLE.md`: scores are always in the number face (Space Grotesk); change tokens, not ad hoc values.

## Tasks
Long-running work starts from a spec in `docs/tasks/<slug>.md` (goal, constraints, acceptance criteria = CI commands above pass + listed behaviors).
