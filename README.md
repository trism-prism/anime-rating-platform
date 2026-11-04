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

## Configuration
Set as environment variables (`__` for nesting) in production, or with `dotnet user-secrets` in `api/` locally. No values are committed.

- `Jwt__Key` signing key, 32+ characters (Development has a dev-only one)
- `Jwt__Issuer`
- `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__From` (without a password the confirmation link is logged to the console)
- `App__FrontendUrl` base URL used in email links
- `Cors__Origins` comma separated allowed origins
- `Admin__Email` registered user that gets the Admin role at startup
- `ConnectionStrings__Default`

More setup notes are added as features land.

## API
- `GET /titles?search=&type=&genre=&page=&pageSize=` paged list, most popular first
- `GET /titles/{id}` title with characters, average scores and reviews
- `PUT /titles/{id}/rating` create or update your rating (log in first)
- `DELETE /titles/{id}/rating` remove your rating
- `GET /me/ratings` your ratings
- `GET /me/watchlist` your watch list
- `PUT /me/watchlist/{titleId}` set status and progress
- `DELETE /me/watchlist/{titleId}` remove an entry
