# Auth

Password or Google. No second factor at login. Emailed 6-digit codes are used for three things only: verify email, reset password, confirm account deletion for accounts without a password.

## Tokens

```mermaid
flowchart LR
  subgraph Browser
    M["Access token<br/>JWT, 15 min, memory only"]
    C["Cookie rt_userId<br/>refresh, 30 days<br/>HttpOnly Secure SameSite=Lax<br/>Path=/api/auth"]
    LS["localStorage<br/>accounts (max 5), activeUser<br/>no tokens"]
  end
  subgraph API
    SE["Session row<br/>SHA-256 of current and previous token"]
  end
  C <--> SE
  M -->|"sid claim checked every request"| SE
```

Up to five accounts can be signed in at once, one cookie each. A session lasts 30 days sliding, 90 days at most.

## Sign in and refresh

```mermaid
sequenceDiagram
  participant W as Web
  participant A as API
  W->>A: POST /auth/login
  A->>A: lockout check, password check
  A-->>W: accessToken + Set-Cookie rt_userId
  Note over W: token kept in memory

  W->>A: GET /me/ratings (expired token)
  A-->>W: 401
  W->>A: POST /auth/refresh {userId}
  Note over W: one refresh at a time (navigator.locks)
  A->>A: rotate refresh token
  A-->>W: new accessToken + new cookie
  W->>A: retry once
```

Ten failed logins lock the account for 5 minutes (423). Sensitive actions answer 403 or 400, never 401, so the client does not retry and double-count a wrong password.

## Session states

```mermaid
stateDiagram-v2
  [*] --> Active: login
  Active --> Active: refresh rotates token
  Active --> Revoked: old token reused
  Active --> Revoked: concurrent double refresh
  Active --> Revoked: security stamp changed
  Active --> Revoked: logout
  Active --> Expired: 30 days idle or 90 days total
  Revoked --> [*]: purged daily
  Expired --> [*]: purged daily
```

Password change, email change and password reset rotate the stamp. A password change keeps the current session and ends the rest.

## Register and verify

```mermaid
sequenceDiagram
  actor U as User
  participant W as Web
  participant A as API
  participant G as Gmail
  U->>W: email, password, tick 13+ and Terms
  W->>A: POST /auth/register
  A->>A: length 12+, HIBP check, store Terms version
  A-->>W: created (no email sent, no session)
  U->>W: log in
  W->>A: POST /auth/login
  U->>W: banner → /verify
  W->>A: POST /me/verify/send
  A->>G: 6-digit code
  U->>W: enters code
  W->>A: POST /me/verify (5 tries / 15 min)
  A-->>W: email confirmed
```

Review text, replies and reports need a verified email.

## Google

```mermaid
sequenceDiagram
  participant W as Web
  participant A as API
  participant G as Google
  W->>A: GET /auth/google/start
  A->>A: cookie g_oauth = state.verifier (10 min)
  A-->>W: redirect, PKCE S256
  W->>G: sign in
  G-->>A: GET /auth/google/callback?code&state
  A->>A: drop cookie, compare state
  A->>G: exchange code
  A->>A: check iss, aud, exp, email_verified (signature skipped, token came over TLS from Google)
  alt known Google login
    A->>A: sign in
  else known email, no Google login yet
    A->>A: link, mark email confirmed
  else new account and Terms ticked
    A->>A: create passwordless user
  else new account, Terms not ticked
    A-->>W: /login?error=terms
  end
  A-->>W: session cookie, redirect /auth/callback?userId
  W->>A: POST /auth/refresh
```

If an existing account's email was never verified, linking removes its password and sessions so nobody can pre-claim an address.

## Reset password

```mermaid
flowchart LR
  E["Email"] -->|"POST forgot-password<br/>same answer either way, 60 s cooldown"| C["Code"]
  C -->|"verify, 5 tries / 15 min"| P["New password"]
  P -->|"policy + HIBP"| D["Email confirmed, lockout cleared,<br/>every session revoked"]
```

Google-only accounts use this to add a password.

## Change email, password, username

```mermaid
flowchart TD
  subgraph Email
    E1["POST /me/email<br/>new address + current password"] --> E2["Link to the new address<br/>notice to the old one"]
    E2 --> E3["POST /auth/confirm-email-change<br/>token from the link"]
    E3 --> E4["Email changed, every session revoked"]
  end
  subgraph Password
    P1["POST /me/password<br/>current + new, policy + HIBP"] --> P2["This session kept, the rest revoked"]
  end
  subgraph Username
    U1["PUT /me/username<br/>3-30 of A-Z a-z 0-9 . _ -, unique"] --> U2["Changed, sessions untouched"]
  end
```

A wrong current password answers 403 and counts toward the lockout. Google-only accounts have no password, so they cannot use the email or password routes until they add one through Reset password. A blocked address gets the same silent 200, and sends are limited to one a minute per address and per user.

## Delete and export

```mermaid
flowchart TD
  D[Delete] --> H{Has password?}
  H -->|yes| P[Current password]
  H -->|no| M["POST /me/delete/code → emailed code"]
  P --> L{Last admin?}
  M --> L
  L -->|yes| R[409 refused]
  L -->|no| T["Anonymize, see data.md"]
  T --> N[Notice email]

  X[Export] --> XP[Current password]
  XP --> F{Format}
  F --> J[JSON, one file]
  F --> Z["CSV zip: profile, ratings, reviews,<br/>list entries, lists, list items"]
```

Export needs a password, so it is hidden for Google-only accounts. CSV cells starting with `= + - @`, tab or carriage return get a leading apostrophe.

## First admin

`Admin__Email` names the first admin. The matching user is promoted at login, Google login or email verify, once their email is confirmed and no admin exists. Admin status is re-read from the database on every request.
