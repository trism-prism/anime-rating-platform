# Deploying

API on Railway (with Postgres), website on Vercel. Both deploy from `main` on GitHub.

## 1. Railway (API + database)

1. Go to railway.com, sign in with GitHub, click **New Project > Deploy from GitHub repo** and pick this repo. Leave the root directory empty (the repo root); `railway.json` holds the build and start commands.
2. In the project click **New > Database > Add PostgreSQL**.
3. Open the API service > **Variables** and add (names exactly as written, two underscores):
   - `DATABASE_URL` = `${{Postgres.DATABASE_URL}}` (click "Add Reference" and pick the Postgres one). The app converts it itself. If you set `ConnectionStrings__Default` instead, that wins.
   - `Jwt__Key` = a random string of 48+ characters (for example run `openssl rand -base64 48` in Git Bash). The app refuses to start without it.
   - `Jwt__Issuer` = `anime-rating-platform`
   - `Smtp__User` = your Gmail address
   - `Smtp__Password` = a Gmail app password: Google account > Security > 2-Step Verification (turn it on) > App passwords > create one, copy the 16 letters
   - `Smtp__From` = your Gmail address
   - `Admin__Email` = the email you will register with on the site (that account becomes Admin)
   - `App__FrontendUrl` and `Cors__Origins`: put `http://localhost:5173` for now, you fix them in step 3
4. API service > **Settings**: turn on **Wait for CI** so a deploy only runs after the GitHub checks pass.
5. API service > **Settings > Networking > Generate Domain**. When it asks for the port, enter the port shown in the deploy log line `Now listening on` (Railway sets `PORT`, usually 8080). Open `https://<that-domain>/health`; it should say `ok`.

## 2. Vercel (website)

1. Go to vercel.com, sign in with GitHub, **Add New > Project**, import this repo.
2. Set **Root Directory** to `web`. The Vite settings are detected on their own.
3. Under **Environment Variables** add `VITE_API_URL` = the Railway URL (`https://...up.railway.app`, no trailing slash). Deploy.
4. Copy the Vercel URL (for example `https://anime-rating-platform.vercel.app`).

## 3. Point the API at the website

Back in Railway > Variables set `Cors__Origins` and `App__FrontendUrl` to the Vercel URL (no trailing slash, no path). Railway redeploys by itself.

## 4. Load the anime data (once)

Either:
- Railway CLI: `railway link`, then `railway run dotnet out/api.dll import` (runs on your PC against the Railway database, so use the database's public URL if it complains), or
- register on the site with `Admin__Email`, confirm the email, log in, then call `POST /admin/import` with your token.

## 5. Smoke test

- [ ] `/health` on the Railway domain says `ok`
- [ ] Vercel site loads and a refresh on a deep link (e.g. `/trending`) does not 404
- [ ] Browse page shows anime
- [ ] Register, the confirmation email arrives, the link confirms the account
- [ ] Log in, rate a title, it appears in My List
- [ ] Trending and Rankings pages load
- [ ] Wrong password 5 times locks the account for 15 minutes

## If something breaks

- API crashes at start with `Jwt:Key`: the variable is missing or under 32 characters.
- CORS error in the browser console: `Cors__Origins` does not exactly match the Vercel URL.
- No email: check `Smtp__*` and that the app password has no spaces.
- Database errors: migrations run on every start; check the Railway deploy logs.
