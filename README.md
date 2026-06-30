# Anime Rating Platform

Rate anime and manga on story, art, characters and enjoyment, and write reviews. Titles, characters and voice actors are imported from the AniList GraphQL API.

- `api/` ASP.NET Core 8 Web API, EF Core, PostgreSQL, Identity + JWT
- `api.Tests/` xUnit tests
- `web/` React + Vite + TypeScript

## Run locally
Needs the .NET 8 SDK, Node 20+ and PostgreSQL.

```
cd api && dotnet run
cd web && npm install && npm run dev
```

More setup notes are added as features land.
