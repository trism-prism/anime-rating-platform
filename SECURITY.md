# Security

This is a hobby and portfolio project run by one person. There is no bug bounty and no guaranteed response time, but reports are read and taken seriously.

## How to report a problem

- Use GitHub's private vulnerability reporting: the **Security** tab of this repository, then **Report a vulnerability**.
- Or use the Support form on the website and say it is a security report. Do not put the details in a public issue.

Please include what you found, the steps to reproduce it, and what an attacker could do with it. Do not read, change or delete other people's data, and do not run load or denial-of-service tests.

## In scope

- The API (`/auth`, `/me`, `/admin` and the other routes listed at `/docs`) and the website.
- Broken sign-in, session or password reset logic, and missing or wrong permission checks (for example seeing a review outside its audience, or acting as another user).
- Injection, cross-site scripting, request forgery, and leaks of private data or secrets.

## Out of scope

- Denial of service and volume-based attacks, spam, and social engineering.
- Problems in services we use (Railway, Vercel, Google, Resend, AniList, Have I Been Pwned); report those to the vendor.
- Missing best-practice headers or settings with no demonstrated impact, and findings that need a rooted device or a compromised browser.

## What is already done

- Passwords are hashed by ASP.NET Identity, must be at least 12 characters, and are checked against known breaches with k-anonymity (only a 5 character hash prefix leaves the server). Repeated wrong passwords lock the account for a few minutes.
- Sign-in uses a 15 minute access token in memory and a rotating 30 day refresh token in an HttpOnly, Secure, SameSite cookie. Reusing an old refresh token ends the session. Sessions can be ended per device, and a password or email change signs the other devices out.
- Sensitive actions (change password or email, export, delete account) ask for the current password, or for a fresh emailed code when the account has none. Emailed codes are single purpose, expire within minutes and allow 5 tries per 15 minutes.
- Rate limits on sign-in, reports, replies, reactions, the support form and other write routes.
- Every route that returns a review, reply or list goes through one visibility rule (audience, friendships, blocks); user ids are always read from the signed token, never from the request.
- Security headers: a Content-Security-Policy, HSTS, `X-Content-Type-Options`, a Referrer-Policy, a Permissions-Policy and `frame-ancestors 'none'` on the API (set in `api/Program.cs`) and on the website (`web/vercel.json`). The website has no inline scripts.
- Secrets live in environment variables, not in the repository, and logs avoid codes, links and tokens.
- A daily job removes expired sessions, unverified accounts after 30 days, settled reports after a year and old notifications. Users can export and delete their own data.

## Known limits

- Rate-limit and code-attempt counters are kept in memory, so a restart resets them.
- Pages need `style-src 'unsafe-inline'` because the interface sets some styles from script; scripts are not allowed to be inline.
- The `/docs` page (Swagger UI) needs inline scripts, so it has its own, looser policy. It is served by the API host only and shows no data.
