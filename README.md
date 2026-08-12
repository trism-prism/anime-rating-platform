# GengaRate

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
- `Gmail__ClientId`, `Gmail__ClientSecret`, `Gmail__RefreshToken`, `Gmail__From` (Gmail API sender, used first when the refresh token is set; see DEPLOY.md)
- `Resend__ApiKey`, `Resend__From` (fallback sender; with neither sender set, Development logs the mail, code included, to the console and other environments fail to send)
- `App__FrontendUrl` base URL used in the email-change link and the Google redirect
- `Cors__Origins` comma separated allowed origins (only for local development; in production the site reaches the API through its own `/api` path, see DEPLOY.md)
- `Admin__Email` bootstraps the first admin: that user gets the Admin role at login once their email is verified and no admin exists. Other admins are managed on the site
- `ConnectionStrings__Default`

More setup notes are added as features land.

## API
The full route list with request and response shapes is served by the API itself at `/docs` (Swagger UI; the JSON is at `/docs/v1/openapi.json`). It lists routes only, no secrets. Security reports: see SECURITY.md.

Compliance routes: `POST /support` `{name?, email, message, website}` emails the admins (works signed out; `website` is a honeypot that must stay empty; 5 an hour). `POST /me/delete` `{currentPassword}` or `{code}` anonymizes the account (personal data removed; ratings, reviews, replies and reports stay as "Deleted user"); `POST /me/delete/code` emails the code to accounts without a password. `POST /auth/register` also needs `ageConfirmed` and `termsAccepted`.

- `GET /titles?search=&type=&genre=&page=&pageSize=` paged list, most popular first
- `GET /titles/{id}` title with characters, average scores and reviews
- `POST /auth/login` log in; works before the email is verified and returns a 15 minute `accessToken` and `emailVerified`. A 30 day refresh token is set as an HttpOnly cookie `rt_<userId>`. A right password never asks for a code
- `POST /auth/register` `{email, password, userName?}` create an account (no email is sent; verify it later with a code)
- `POST /auth/forgot-password` `{email}` email a 6-digit code if the account exists; always the same answer, one code per address per minute
- `POST /auth/reset-code/verify` `{email, code}` answers `{ok}`; 5 tries per address per 15 minutes, then 429
- `POST /auth/reset-password` `{email, code, newPassword}` set a new password (policy and breach check apply), verify the email, clear the lockout and sign every device out. Google-only accounts use it to add a password
- `POST /auth/refresh` `{userId}` swap the refresh cookie for a new one and a new access token; reusing an old refresh token ends the session
- `POST /auth/logout` `{userId}` end that session and delete its cookie
- `GET /me` your email, user name, `emailVerified` and `isAdmin`
- `PUT /me/username` `{userName}` change the display name shown on reviews (3 to 30 letters, digits, `.`, `_`, `-`; unique; no password needed)
- `POST /me/verify/send` email a 6-digit code to the account address, at most one per 30 seconds
- `POST /me/verify` `{code}` verify the email with it; 5 tries per 15 minutes, then 429. Review text needs a verified email
- `POST /me/password` `{currentPassword, newPassword}` change the password; other devices are signed out. Wrong current password answers 403 and counts toward the lockout
- `POST /me/email` `{newEmail, currentPassword}` mail a confirm link to the new address (and a notice to the old one); `POST /auth/confirm-email-change` `{userId, newEmail, token}` applies it and signs every device out
- `GET /admin/users?search=&page=&pageSize=` list users (admin)
- `POST /admin/users/{id}/admin` make a user admin, `DELETE` removes it; the last admin cannot be removed (admin)
- `POST /admin/import` import titles from AniList (admin)
- `PUT /titles/{id}/rating` create or update your rating (log in first; review text needs a verified email)
- `DELETE /titles/{id}/rating` remove your rating
- `GET /me/ratings` your ratings
- `GET /trending?type=Anime|Manga` what is trending on AniList right now, limited to titles we have
- `GET /rankings?category=overall|story|art|characters|enjoyment&type=&take=20` best rated titles (at least 3 ratings)
- `GET /me/watchlist` your watch list
- `PUT /me/watchlist/{titleId}` set status and progress
- `DELETE /me/watchlist/{titleId}` remove an entry
