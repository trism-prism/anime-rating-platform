# Deploying

API on Railway (with Postgres), website on Vercel. Both deploy from `main` on GitHub.

## 1. Railway (API + database)

1. Go to railway.com, sign in with GitHub, click **New Project > Deploy from GitHub repo** and pick this repo. Leave the root directory empty (the repo root); `railway.json` holds the build and start commands.
2. In the project click **New > Database > Add PostgreSQL**.
3. Open the API service > **Variables** and add (names exactly as written, two underscores):
   - `DATABASE_URL` = `${{Postgres.DATABASE_URL}}` (click "Add Reference" and pick the Postgres one). The app converts it itself. If you set `ConnectionStrings__Default` instead, that wins.
   - `Jwt__Key` = a random string of 48+ characters (for example run `openssl rand -base64 48` in Git Bash). The app refuses to start without it.
   - `Jwt__Issuer` = `anime-rating-platform`
   - `Resend__ApiKey` = an API key from resend.com > API Keys > Create API Key (sending access). Paste it only into Railway, never into chat or the repo. Railway blocks outbound SMTP, so mail goes through Resend's HTTPS API.
   - `Resend__From` = `onboarding@resend.dev` until you have a domain (see below)
   - Resend limit: without a verified domain, Resend only delivers to the email of your own Resend account, and the sender must be `onboarding@resend.dev`. To email other users, resend.com > Domains > add and verify a domain (DNS records), then set `Resend__From` to an address on it, for example `noreply@yourdomain.com`.
   - `Admin__Email` = the email you will register with on the site. It only bootstraps the first admin: once that account has verified its email and logs in, it becomes Admin (only while no admin exists). Add or remove other admins on the site's Admin page, so no Railway change is needed later. Emergency recovery: if every admin is locked out, set `Admin__Email` to another verified account; it only works while no admin exists
   - `App__FrontendUrl`: put `http://localhost:5173` for now, you fix it in step 3
   - `Cors__Origins` = `http://localhost:5173`. The site calls the API through its own `/api` path (see step 2), so CORS only matters when running locally.
4. API service > **Settings**: turn on **Wait for CI** so a deploy only runs after the GitHub checks pass.
5. API service > **Settings > Networking > Generate Domain**. When it asks for the port, enter the port shown in the deploy log line `Now listening on` (Railway sets `PORT`, usually 8080). Open `https://<that-domain>/health`; it should say `ok`.

## 2. Vercel (website)

1. Go to vercel.com, sign in with GitHub, **Add New > Project**, import this repo.
2. Set **Root Directory** to `web`. The Vite settings are detected on their own.
3. No environment variables are needed. The site calls `/api/...` on its own address and `web/vercel.json` forwards that to the Railway API (so the login cookie belongs to the site, not to Railway). If your Railway domain differs from the one in `web/vercel.json`, change the rewrite there. Deploy.
4. Copy the Vercel URL (for example `https://anime-rating-platform.vercel.app`).

## 3. Point the API at the website

Back in Railway > Variables set `App__FrontendUrl` to the Vercel URL (no trailing slash, no path). `Cors__Origins` is not needed in production. Railway redeploys by itself.

## 4. Load the anime data (once)

Either:
- Railway CLI: `railway link`, then `railway run dotnet out/api.dll import` (runs on your PC against the Railway database, so use the database's public URL if it complains), or
- register on the site with `Admin__Email`, confirm the email (link or `POST /me/verify-email`), log in again so you get the Admin role, then use the import button on the site's Admin page.

## 5. Smoke test

- [ ] `/health` on the Railway domain says `ok`
- [ ] Vercel site loads and a refresh on a deep link (e.g. `/trending`) does not 404
- [ ] Browse page shows anime
- [ ] Register, the confirmation email arrives, the link confirms the account
- [ ] Log in works even before the email is confirmed; posting review text needs a confirmed email
- [ ] Log in, rate a title, it appears in My List
- [ ] Trending and Rankings pages load
- [ ] Wrong password 5 times locks the account for 15 minutes

## Free email with the Gmail API

Sends from your own Gmail to any recipient, over HTTPS (Railway blocks SMTP). When `Gmail__RefreshToken` is set it is used instead of Resend.

1. Go to console.cloud.google.com, click the project picker at the top > **New Project**, name it and create it.
2. **APIs & Services > Library**, search "Gmail API" > **Enable**.
3. **APIs & Services > OAuth consent screen**: user type **External** > Create. Fill in the app name and your email. Under **Test users** add your own Gmail address. Under **Scopes** add `https://www.googleapis.com/auth/gmail.send`. Then click **Publish app** (status "In production"), otherwise the refresh token expires after 7 days. The "unverified app" warning you see later is normal for personal use.
4. **APIs & Services > Credentials > Create Credentials > OAuth client ID**: application type **Web application**. Under **Authorized redirect URIs** add `https://developers.google.com/oauthplayground` > Create. Keep the client ID and client secret.
5. Open developers.google.com/oauthplayground. Click the gear icon > tick **Use your own OAuth credentials** > paste the client ID and secret. In step 1 on the left enter the scope `https://www.googleapis.com/auth/gmail.send` > **Authorize APIs**, sign in with your Gmail and continue past the unverified-app warning. In step 2 click **Exchange authorization code for tokens** and copy the **Refresh token**.
6. In Railway > API service > Variables add (you type the secrets in Railway yourself, never paste them in chat or the repo):
   - `Gmail__ClientId`
   - `Gmail__ClientSecret`
   - `Gmail__RefreshToken`
   - `Gmail__From` = `Anime Rating Platform <youraddress@gmail.com>`

Gmail allows only a few hundred sends a day for a normal account. `Gmail__*` wins over `Resend__*` when both are set.

## If something breaks

- API crashes at start with `Jwt:Key`: the variable is missing or under 32 characters.
- Requests to `/api/...` fail or return the site's HTML: the rewrite in `web/vercel.json` points at the wrong Railway domain.
- Everyone is asked to log in again after a few minutes: the browser is not sending the refresh cookie; check that calls go to `/api/auth/refresh` on the Vercel domain, not straight to Railway.
- No email: check that `Resend__ApiKey` is set and `Resend__From` is correct.
- Resend 403 validation error in the logs = unverified domain or wrong From: use `onboarding@resend.dev` (and only email your own Resend address), or verify a domain and use an address on it.
- Gmail "token request failed: 400" (`invalid_grant`): the refresh token was revoked or expired, usually because the consent screen is still in Testing. Publish the app and create a new token.
- Gmail "rejected the email: 403": the Gmail API is not enabled for the project (APIs & Services > Library).
- Database errors: migrations run on every start; check the Railway deploy logs.
