## Hosting Plan

This repo is set up for:

- Frontend: Vercel Hobby
- Backend: Render free web service
- Database: Supabase Postgres free tier

### 1. Backend on Render

Render uses the root [`render.yaml`](/c:/Users/arung/TradingAgent/render.yaml) file and the backend Dockerfile at [Dockerfile](/c:/Users/arung/TradingAgent/KrishAgent/Dockerfile).
The checked-in Blueprint is pinned to the `main` branch so production tracks the live branch directly.

Create a new Render Blueprint or Web Service from the GitHub repo and set these environment variables:

- `DATABASE_URL`
  Use your Supabase Postgres connection string or URL.
- `CORS_ALLOWED_ORIGINS`
  Example: `https://your-app.vercel.app,https://your-custom-domain.com`
- `Alpaca__ApiKey`
- `Alpaca__SecretKey`
- `Alpaca__Feed`
  Keep `iex` if you want the free market-data feed.
- `OpenAI__ApiKey`
  Optional. If omitted, the app falls back to rule-based analysis.
- `OpenAI__Model`
  Optional. Default suggestion: `gpt-5-mini`

The app automatically runs EF Core migrations on startup.

### 2. Database on Supabase

Create a free Supabase project and copy the Postgres connection string.

Recommended:

- Use the direct Postgres connection string or `DATABASE_URL`
- Keep SSL enabled

This backend supports both:

- SQLite locally
- Postgres in production

### 3. Frontend on Vercel

Import the repo into Vercel and set the project root to `krishagentui`.
Set the Production Branch to `main` so Vercel tracks the same branch as the Render backend.

Set this environment variable in Vercel:

- `NEXT_PUBLIC_API_BASE`
  Example: `https://krish-agent-api.onrender.com/api`

Every push to GitHub will trigger:

- a new Render backend deploy
- a new Vercel frontend deploy

### 4. Branching model

Use this branch flow going forward:

- `main`
  Production-ready code that stays deployed.
- `dev`
  Integration branch for upcoming work.
- `feature/<name>`
  Short-lived branches created from `dev`, then merged back into `dev`.

When a set of features is stable in `dev`, merge `dev` into `main` to release it.

### 5. Local vs Production

Local development still uses SQLite from [appsettings.json](/c:/Users/arung/TradingAgent/KrishAgent/appsettings.json).

Production switches automatically to Postgres when `DATABASE_URL` or a Postgres-style `ConnectionStrings__DefaultConnection` is present.

### 6. Free-tier caveats

- Render free web services sleep after inactivity, so the first request can be slow.
- Supabase free projects have usage limits.
- OpenAI is optional but not free.
- Alpaca `iex` is free but not full-market SIP coverage.
